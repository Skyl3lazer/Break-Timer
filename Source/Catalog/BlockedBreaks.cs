using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakTimer
{
    public enum BlockKind { RequirementUnmet, IdeoDisallowed }

    public readonly struct BlockedBreak
    {
        public BlockedBreak(BreakInfo info, BlockKind kind)
        {
            Info = info;
            Kind = kind;
        }

        public BreakInfo Info { get; }
        public BlockKind Kind { get; }
    }

    // Suppressed is the (Effective, Eligible] range Dubs Break Mod holds back; empty when it is not constraining.
    public readonly struct ResolvedTiers
    {
        public ResolvedTiers(MentalBreakIntensity? effective, MentalBreakIntensity? eligible, IReadOnlyList<MentalBreakIntensity> suppressed)
        {
            Effective = effective;
            Eligible = eligible;
            Suppressed = suppressed;
        }

        public MentalBreakIntensity? Effective { get; }
        public MentalBreakIntensity? Eligible { get; }
        public IReadOnlyList<MentalBreakIntensity> Suppressed { get; }
    }

    // Split from the tooltip so the selection policy can be tested without the rendering.
    public static class BlockedBreaks
    {
        // Omits vanilla's 2000-tick dwell requirement - too coarse for a snapshot tooltip.
        public static MentalBreakIntensity? HighestEligibleIntensity(MentalBreaker breaker)
        {
            float mood = breaker.CurMood;
            if (mood < breaker.BreakThresholdExtreme) return MentalBreakIntensity.Extreme;
            if (mood < breaker.BreakThresholdMajor) return MentalBreakIntensity.Major;
            if (mood < breaker.BreakThresholdMinor) return MentalBreakIntensity.Minor;
            return null;
        }

        public static ResolvedTiers ResolveTiers(Pawn pawn, MentalBreaker breaker)
            => CombineTiers(HighestEligibleIntensity(breaker), DubsBreakModCompat.GrievanceCap(pawn));

        // cap null means no Dubs Break Mod constraint; a cap above the eligible tier suppresses nothing.
        public static ResolvedTiers CombineTiers(MentalBreakIntensity? eligible, MentalBreakIntensity? cap)
        {
            if (cap is null)
                return new ResolvedTiers(eligible, eligible, Array.Empty<MentalBreakIntensity>());

            int eligibleRank = eligible.HasValue ? (int)eligible.Value : 0;
            int effectiveRank = Math.Min(eligibleRank, (int)cap.Value);

            MentalBreakIntensity? effective = effectiveRank == 0 ? null : (MentalBreakIntensity)effectiveRank;
            var suppressed = new List<MentalBreakIntensity>();
            for (int rank = effectiveRank + 1; rank <= eligibleRank; rank++)
                suppressed.Add((MentalBreakIntensity)rank);

            return new ResolvedTiers(effective, eligible, suppressed);
        }

        // Vanilla sets this on recovery only. A non-zero value means the pawn just came out of a break.
        public static int MoodBreakCooldownTicks(MentalBreaker? breaker)
        {
            if (breaker is null) return 0;
            try { return Math.Max(0, cooldownField(breaker)); }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[BreakTimer] ticksUntilCanDoMentalBreak unreadable: {ex.Message}",
                    Once.Id("break-cooldown"));
                return 0;
            }
        }

        static readonly AccessTools.FieldRef<MentalBreaker, int> cooldownField =
            AccessTools.FieldRefAccess<MentalBreaker, int>("ticksUntilCanDoMentalBreak");

        // Mirrors MentalBreaker.ChooseAnomalyBreak, which is private.
        public static float AnomalyBreakChance(Pawn pawn)
        {
            if (pawn is null || !ModsConfig.AnomalyActive) return 0f;
            try
            {
                if (pawn.IsCreepJoiner) return 1f;
                return Mathf.Clamp01(Find.Anomaly?.LevelDef?.anomalyMentalBreakChance ?? 0f);
            }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[BreakTimer] anomalyMentalBreakChance unreadable: {ex.Message}",
                    Once.Id("anomaly-chance"));
                return 0f;
            }
        }

        // Vanilla's draw is two-stage. One merged pool would misweight both halves.
        public static List<(BreakInfo info, float weight)> MoodTierEntries(Pawn pawn, MentalBreakIntensity tier)
        {
            var anomalous = new List<(BreakInfo info, float weight)>(2);
            var mundane = new List<(BreakInfo info, float weight)>(8);
            if (pawn is null) return mundane;

            bool anomalyActive = ModsConfig.AnomalyActive;
            HashSet<MentalBreakDef>? ideoAllow = IdeoFilterFor(pawn);

            foreach (BreakInfo info in MentalBreakCatalog.OfIntensity(tier))
            {
                try
                {
                    if (info.Requirements.AnomalousBreak && !anomalyActive) continue;
                    if (ideoAllow != null && !ideoAllow.Contains(info.Def)) continue;
                    if (!info.CanOccurFor(pawn)) continue;

                    float weight = info.CommonalityFor(pawn, moodCaused: true);
                    if (weight <= 0f || float.IsNaN(weight) || float.IsInfinity(weight)) continue;

                    (info.Requirements.AnomalousBreak ? anomalous : mundane).Add((info, weight));
                }
                catch (Exception ex)
                {
                    Log.WarningOnce(
                        $"[BreakTimer] Skipping break {info.DefName} in possible-list: {ex.Message}",
                        Once.Id("possible-list", info.DefName));
                }
            }

            // An empty pool cannot be drawn from. Its share collapses onto the other.
            float anomalyShare =
                anomalous.Count == 0 ? 0f :
                mundane.Count == 0 ? 1f :
                AnomalyBreakChance(pawn);

            var entries = new List<(BreakInfo info, float weight)>(anomalous.Count + mundane.Count);
            AppendScaled(entries, anomalous, anomalyShare);
            AppendScaled(entries, mundane, 1f - anomalyShare);
            return entries;
        }

        static void AppendScaled(
            List<(BreakInfo info, float weight)> sink,
            List<(BreakInfo info, float weight)> pool,
            float share)
        {
            if (pool.Count == 0 || share <= 0f) return;

            float total = 0f;
            foreach (var e in pool) total += e.weight;
            if (total <= 0f) return;

            foreach (var e in pool) sink.Add((e.info, share * e.weight / total));
        }

        // Null when the pawn's ideoligion names no breaks, which is vanilla's cue to ignore it.
        static HashSet<MentalBreakDef>? IdeoFilterFor(Pawn pawn)
        {
            HashSet<MentalBreakDef>? allow = null;
            try { allow = pawn.Ideo?.cachedPossibleMentalBreaks; }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[BreakTimer] Ideo.cachedPossibleMentalBreaks threw: {ex.Message}",
                    Once.Id("ideo-breaks"));
            }
            return allow != null && allow.Count > 0 ? allow : null;
        }

        // Anomaly-gated breaks are dropped when the DLC is off so callers never advertise absent DLC.
        public static List<BlockedBreak> InTier(Pawn pawn, MentalBreakIntensity tier)
        {
            var result = new List<BlockedBreak>();
            if (pawn is null) return result;

            bool anomalyActive = ModsConfig.AnomalyActive;
            HashSet<MentalBreakDef>? ideoAllow = IdeoFilterFor(pawn);

            foreach (BreakInfo info in MentalBreakCatalog.OfIntensity(tier))
            {
                try
                {
                    if (info.Requirements.AnomalousBreak && !anomalyActive) continue;

                    bool ideoDisallowed = ideoAllow != null && !ideoAllow.Contains(info.Def);
                    if (!ideoDisallowed && info.CanOccurFor(pawn)) continue;

                    result.Add(new BlockedBreak(info, ideoDisallowed ? BlockKind.IdeoDisallowed : BlockKind.RequirementUnmet));
                }
                catch (Exception ex)
                {
                    Log.WarningOnce(
                        $"[BreakTimer] Blocked-list scan failed for {info.DefName}: {ex.Message}",
                        Once.Id("blocked-list", info.DefName));
                }
            }

            return result;
        }
    }
}
