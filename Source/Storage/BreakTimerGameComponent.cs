using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakTimer
{
	/// <summary>
	/// Per-save store of currently-active mental breaks and a short per-pawn history of
	/// recent ones. Created automatically by RimWorld whenever a game is loaded or started
	/// (any subclass of <see cref="GameComponent"/> with a <c>(Game)</c> constructor is
	/// auto-discovered), so no def or About.xml entry is required.
	/// </summary>
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

		/// <summary>
		/// Called by the start-state Harmony patch once a new <see cref="MentalState"/>
		/// has been successfully attached to a pawn.
		/// </summary>
		public void OnBreakStarted(Pawn pawn, MentalState state, string? reason)
		{
			if (pawn is null || state?.def is null) return;

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

		/// <summary>
		/// Called by the recovery Harmony patch when a <see cref="MentalState"/> ends.
		/// Archives the active record into history.
		/// </summary>
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

		void ArchiveRecord(Pawn pawn, ActiveBreakRecord rec, int endTick)
		{
			if (rec?.stateDef is null) return;
			if (!history.TryGetValue(pawn, out PawnBreakHistory bucket))
			{
				bucket = new PawnBreakHistory();
				history[pawn] = bucket;
			}
			bucket.Add(new CompletedBreakRecord(rec, endTick), HistoryPerPawn);
		}

		/// <summary>
		/// After a save loads, scan every pawn that's already in a mental state. For any
		/// without an <see cref="ActiveBreakRecord"/> (e.g., saved before this mod was
		/// installed), synthesize one using <see cref="MentalState.Age"/> as a stand-in
		/// for the original start tick. Idempotent.
		/// </summary>
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
				if (kv.Key is null || kv.Value?.stateDef is null)
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
