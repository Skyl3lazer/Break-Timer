using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BreakTimer.Patches
{
    // Records the tick a break begins. Skipped when the state didn't start, or before the
    // BreakTimerGameComponent exists (very early init, before Current.Game).
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class MentalStateHandler_TryStartMentalState_Patch
    {
        static void Postfix(
            MentalStateHandler __instance,
            bool __result,
            string? reason)
        {
            if (!__result) return;

            // Invalidate even if the component isn't ready yet, or the hover string lags
            // the transition by up to the cache TTL.
            BreakIndicator.InvalidateTooltipCache();

            BreakTimerGameComponent? store = BreakTimerGameComponent.Instance;
            if (store is null) return;

            try
            {
                MentalState? state = __instance.CurState;
                if (state?.pawn is null || state.def is null) return;

                store.OnBreakStarted(state.pawn, state, reason);
            }
            catch (Exception ex)
            {
                Log.Error($"[BreakTimer] TryStartMentalState postfix failed: {ex}");
            }
        }
    }
}
