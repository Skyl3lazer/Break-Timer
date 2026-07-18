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
