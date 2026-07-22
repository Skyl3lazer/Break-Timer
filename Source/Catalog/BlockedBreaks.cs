using System;
using System.Collections.Generic;
using RimWorld;
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

        // Anomaly-gated breaks are dropped when the DLC is off so callers never advertise absent DLC.
        public static List<BlockedBreak> InTier(Pawn pawn, MentalBreakIntensity tier)
        {
            var result = new List<BlockedBreak>();
            if (pawn is null) return result;

            bool anomalyActive = ModsConfig.AnomalyActive;
            HashSet<MentalBreakDef>? ideoAllow = null;
            try { ideoAllow = pawn.Ideo?.cachedPossibleMentalBreaks; }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[BreakTimer] Ideo.cachedPossibleMentalBreaks threw: {ex.Message}",
                    Once.Id("ideo-breaks"));
            }
            bool useIdeoFilter = ideoAllow != null && ideoAllow.Count > 0;

            foreach (BreakInfo info in MentalBreakCatalog.OfIntensity(tier))
            {
                try
                {
                    if (info.Requirements.AnomalousBreak && !anomalyActive) continue;

                    bool ideoDisallowed = useIdeoFilter && !ideoAllow!.Contains(info.Def);
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
