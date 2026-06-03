using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace BreakTimer
{
    // Reference catalog of every MentalBreakDef, resolved into BreakInfo records. Built once
    // at startup for fast lookups by intensity or owning mental state.
    public static class MentalBreakCatalog
    {
        static readonly object buildLock = new();
        static bool built;

        static BreakInfo[] all = Array.Empty<BreakInfo>();
        static Dictionary<MentalBreakIntensity, BreakInfo[]> byIntensity = new();
        static Dictionary<MentalStateDef, BreakInfo> byState = new();
        static Dictionary<MentalStateDef, MentalStateInfo> stateInfoByDef = new();

        // The BreakInfo whose mental state matches, if any.
        public static BreakInfo? GetForState(MentalStateDef? state)
        {
            if (state is null) return null;
            EnsureBuilt();
            return byState.TryGetValue(state, out BreakInfo info) ? info : null;
        }

        // MentalStateInfo for any MentalStateDef, with or without a MentalBreakDef. Covers the
        // "pawn is in state X — how long and what's it called?" cases that the break path
        // doesn't own: hediff-driven states (WanderConfused), trait/mental-fit givers, etc.
        public static MentalStateInfo? GetStateInfo(MentalStateDef? state)
        {
            if (state is null) return null;
            EnsureBuilt();
            return stateInfoByDef.TryGetValue(state, out MentalStateInfo info) ? info : null;
        }

        public static IReadOnlyList<BreakInfo> OfIntensity(MentalBreakIntensity intensity)
        {
            EnsureBuilt();
            return byIntensity.TryGetValue(intensity, out BreakInfo[] arr) ? arr : Array.Empty<BreakInfo>();
        }

        // Builds the catalog if it hasn't been built. Idempotent; called from BreakTimerMod
        // at startup.
        public static void EnsureBuilt()
        {
            if (built) return;
            lock (buildLock)
            {
                if (built) return;
                Build();
                built = true;
            }
        }

        static void Build()
        {
            List<MentalBreakDef> defs = DefDatabase<MentalBreakDef>.AllDefsListForReading;
            var infos = new List<BreakInfo>(defs.Count);

            byState = new Dictionary<MentalStateDef, BreakInfo>(defs.Count);

            for (int i = 0; i < defs.Count; i++)
            {
                MentalBreakDef def = defs[i];
                BreakInfo info;
                try
                {
                    info = new BreakInfo(def);
                }
                catch (Exception ex)
                {
                    Log.Error($"[BreakTimer] Failed to build BreakInfo for {def?.defName ?? "<null>"}: {ex}");
                    continue;
                }

                infos.Add(info);
                if (info.MentalState != null && !byState.ContainsKey(info.MentalState))
                    byState[info.MentalState] = info;
            }

            all = infos.ToArray();

            byIntensity = all
                .GroupBy(b => b.Intensity)
                .ToDictionary(g => g.Key, g => g.ToArray());

            List<MentalStateDef> stateDefs = DefDatabase<MentalStateDef>.AllDefsListForReading;
            stateInfoByDef = new Dictionary<MentalStateDef, MentalStateInfo>(stateDefs.Count);
            for (int i = 0; i < stateDefs.Count; i++)
            {
                MentalStateDef sdef = stateDefs[i];
                if (sdef == null) continue;
                try
                {
                    stateInfoByDef[sdef] = new MentalStateInfo(sdef);
                }
                catch (Exception ex)
                {
                    Log.Error($"[BreakTimer] Failed to build MentalStateInfo for {sdef.defName}: {ex}");
                }
            }

            if (Prefs.DevMode)
                Log.Message($"[BreakTimer] Cached {all.Length} mental break defs across {byIntensity.Count} intensity buckets, and {stateInfoByDef.Count} mental state defs.");
        }
    }
}
