using System;
using HarmonyLib;
using Verse;

namespace BreakTimer.Patches
{
    // Catatonic ends when its CatatonicBreakdown hediff is removed, not via RecoverFromState,
    // so archive the active record here. Fires on every hediff removal but the def compare
    // makes non-catatonic removals nearly free.
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
    public static class Pawn_HealthTracker_RemoveHediff_Patch
    {
        static void Postfix(Hediff hediff)
        {
            if (hediff?.def is null || hediff.def != BreakTimerDefOf.CatatonicBreakdown) return;

            Pawn? pawn = hediff.pawn;
            if (pawn is null) return;

            BreakIndicator.InvalidateTooltipCache();

            BreakTimerGameComponent? store = BreakTimerGameComponent.Instance;
            if (store is null) return;

            try
            {
                store.OnHediffBreakEnded(pawn, BreakTimerDefOf.Catatonic);
            }
            catch (Exception ex)
            {
                Log.Error($"[BreakTimer] Catatonic RemoveHediff postfix failed: {ex}");
            }
        }
    }
}
