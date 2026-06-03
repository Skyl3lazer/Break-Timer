using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BreakTimer.Patches
{
    // Archives the active record into history and clears it. Fires for natural recoveries
    // (MTB roll, downed, sleep) and transition recoveries from TryStartMentalState.
    [HarmonyPatch(typeof(MentalState), nameof(MentalState.RecoverFromState))]
    public static class MentalState_RecoverFromState_Patch
    {
        static void Postfix(MentalState __instance)
        {
            BreakIndicator.InvalidateTooltipCache();

            BreakTimerGameComponent? store = BreakTimerGameComponent.Instance;
            if (store is null) return;

            try
            {
                if (__instance?.pawn is null) return;
                store.OnBreakEnded(__instance.pawn, __instance);
            }
            catch (Exception ex)
            {
                Log.Error($"[BreakTimer] RecoverFromState postfix failed: {ex}");
            }
        }
    }
}
