using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakTimer
{
    // Per-save store of active mental breaks plus a short per-pawn history. RimWorld
    // auto-discovers any GameComponent with a (Game) constructor, so no def or About.xml
    // entry is needed.
    public sealed class BreakTimerGameComponent : GameComponent
    {
        public const int HistoryPerPawn = 5;

        Dictionary<Pawn, ActiveBreakRecord> active = new();
        Dictionary<Pawn, PawnBreakHistory> history = new();

        List<Pawn>? scratchActiveKeys;
        List<ActiveBreakRecord>? scratchActiveValues;
        List<Pawn>? scratchHistoryKeys;
        List<PawnBreakHistory>? scratchHistoryValues;

        public BreakTimerGameComponent(Game game) { }

        public static BreakTimerGameComponent? Instance => Current.Game?.GetComponent<BreakTimerGameComponent>();

        public IReadOnlyDictionary<Pawn, ActiveBreakRecord> ActiveBreaks => active;

        public ActiveBreakRecord? GetActive(Pawn? pawn)
        {
            if (pawn is null) return null;
            return active.TryGetValue(pawn, out ActiveBreakRecord rec) ? rec : null;
        }

        public IReadOnlyList<CompletedBreakRecord> GetHistory(Pawn? pawn)
        {
            if (pawn is null) return Array.Empty<CompletedBreakRecord>();
            return history.TryGetValue(pawn, out PawnBreakHistory list) && list?.records != null
                ? list.records
                : (IReadOnlyList<CompletedBreakRecord>)Array.Empty<CompletedBreakRecord>();
        }

        // The indicator only ever renders on a Mood need bar the player controls
        public static bool ShouldTrack(Pawn? pawn)
        {
            if (pawn?.needs?.mood is null) return false;
            return pawn.Faction == Faction.OfPlayer || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony;
        }

        // Called by the start-state patch once a new MentalState is attached to a pawn.
        public void OnBreakStarted(Pawn pawn, MentalState state, string? reason)
        {
            if (pawn is null || state?.def is null) return;
            if (!ShouldTrack(pawn)) return;

            MentalStateDef stateDef = state.def;
            MentalBreakDef? breakDef = MentalBreakCatalog.GetForState(stateDef)?.Def;

            int startTick = Find.TickManager.TicksGame - Mathf.Max(0, state.Age);

            var record = new ActiveBreakRecord(
                stateDef,
                breakDef,
                startTick,
                state.causedByMood,
                state.causedByDamage,
                state.causedByPsycast,
                state.causedByPawn,
                reason);

            if (active.TryGetValue(pawn, out ActiveBreakRecord prior))
                ArchiveRecord(pawn, prior, startTick);

            active[pawn] = record;

            if (Prefs.DevMode)
                Log.Message($"[BreakTimer] Start: {pawn.LabelShort} entered {stateDef.defName} at tick {startTick} (reason: {reason ?? "n/a"}).");
        }

        // Records a break that has no MentalState (its worker applies a hediff, e.g.
        // Catatonic). Mirrors OnBreakStarted but keys off the MentalBreakDef.
        public void OnHediffBreakStarted(Pawn pawn, MentalBreakDef? breakDef, string? reason, bool causedByMood)
        {
            if (pawn is null || breakDef is null) return;
            if (!ShouldTrack(pawn)) return;

            int startTick = Find.TickManager.TicksGame;
            var record = new ActiveBreakRecord(
                null,
                breakDef,
                startTick,
                causedByMood,
                causedByDamage: false,
                causedByPsycast: false,
                causedByPawn: null,
                reason);

            if (active.TryGetValue(pawn, out ActiveBreakRecord prior))
                ArchiveRecord(pawn, prior, startTick);

            active[pawn] = record;

            if (Prefs.DevMode)
                Log.Message($"[BreakTimer] Start: {pawn.LabelShort} entered {breakDef.defName} (hediff break) at tick {startTick} (reason: {reason ?? "n/a"}).");
        }

        // Called by the recovery patch when a MentalState ends; archives the active record.
        public void OnBreakEnded(Pawn pawn, MentalState state)
        {
            if (pawn is null) return;
            if (!active.TryGetValue(pawn, out ActiveBreakRecord rec)) return;

            int endTick = Find.TickManager.TicksGame;
            ArchiveRecord(pawn, rec, endTick);
            active.Remove(pawn);

            if (Prefs.DevMode)
                Log.Message($"[BreakTimer] End: {pawn.LabelShort} recovered from {rec.stateDef?.defName ?? "?"} after {endTick - rec.startTick} ticks.");
        }

        // Ends a hediff-driven break (e.g. Catatonic) when its hediff is removed. Only archives
        // when the active record matches breakDef, so an incidental hediff removal doesn't
        // clobber an unrelated record.
        public void OnHediffBreakEnded(Pawn pawn, MentalBreakDef? breakDef)
        {
            if (pawn is null) return;
            if (!active.TryGetValue(pawn, out ActiveBreakRecord rec)) return;
            if (breakDef != null && rec?.breakDef != null && rec.breakDef != breakDef) return;

            int endTick = Find.TickManager.TicksGame;
            ArchiveRecord(pawn, rec, endTick);
            active.Remove(pawn);

            if (Prefs.DevMode)
                Log.Message($"[BreakTimer] End: {pawn.LabelShort} recovered from {rec?.breakDef?.defName ?? rec?.stateDef?.defName ?? "?"} (hediff break) after {endTick - (rec?.startTick ?? endTick)} ticks.");
        }

        void ArchiveRecord(Pawn pawn, ActiveBreakRecord? rec, int endTick)
        {
            if (rec is null || (rec.stateDef is null && rec.breakDef is null)) return;
            if (!history.TryGetValue(pawn, out PawnBreakHistory bucket))
            {
                bucket = new PawnBreakHistory();
                history[pawn] = bucket;
            }
            bucket.Add(new CompletedBreakRecord(rec, endTick), HistoryPerPawn);
        }

        // After a save loads, scan every pawn already in a mental state and synthesize a
        // record for any without one (e.g. saved before this mod was installed), using
        // MentalState.Age as a stand-in for the start tick. Idempotent.
        public override void LoadedGame() => BackfillFromWorld();

        public override void FinalizeInit() => BackfillFromWorld();

        void BackfillFromWorld()
        {
            Game game = Current.Game;
            if (game?.World == null) return;

            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
            {
                MentalState? state = pawn?.MentalState;
                if (state?.def is null) continue;
                if (!ShouldTrack(pawn)) continue;
                if (active.ContainsKey(pawn!)) continue;

                int startTick = Find.TickManager.TicksGame - Mathf.Max(0, state.Age);
                MentalBreakDef? breakDef = MentalBreakCatalog.GetForState(state.def)?.Def;

                active[pawn!] = new ActiveBreakRecord(
                    state.def,
                    breakDef,
                    startTick,
                    state.causedByMood,
                    state.causedByDamage,
                    state.causedByPsycast,
                    state.causedByPawn,
                    reason: null);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(
                ref active,
                "activeBreaks",
                LookMode.Reference,
                LookMode.Deep,
                ref scratchActiveKeys,
                ref scratchActiveValues);

            Scribe_Collections.Look(
                ref history,
                "breakHistory",
                LookMode.Reference,
                LookMode.Deep,
                ref scratchHistoryKeys,
                ref scratchHistoryValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                active ??= new Dictionary<Pawn, ActiveBreakRecord>();
                history ??= new Dictionary<Pawn, PawnBreakHistory>();
                PurgeNullKeys();
            }
        }

        void PurgeNullKeys()
        {
            List<Pawn>? dropActive = null;
            foreach (var kv in active)
            {
                if (kv.Key is null || kv.Value is null || (kv.Value.stateDef is null && kv.Value.breakDef is null))
                    (dropActive ??= new List<Pawn>()).Add(kv.Key!);
            }
            if (dropActive != null)
                foreach (var p in dropActive) active.Remove(p);

            List<Pawn>? dropHistory = null;
            foreach (var kv in history)
            {
                if (kv.Key is null || kv.Value?.records is null)
                    (dropHistory ??= new List<Pawn>()).Add(kv.Key!);
            }
            if (dropHistory != null)
                foreach (var p in dropHistory) history.Remove(p);
        }
    }
}
