using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BreakTimer.Patches
{
	/// <summary>
	/// Postfix on <see cref="MentalStateHandler.TryStartMentalState"/> that records the
	/// exact tick a mental break begins on a pawn. Skipped when the call returned false
	/// (state didn't actually start) or when no <see cref="BreakTimerGameComponent"/> is
	/// available yet (e.g., during very early init before <c>Current.Game</c> exists).
	/// </summary>
	[HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
	public static class MentalStateHandler_TryStartMentalState_Patch
	{
		static void Postfix(
			MentalStateHandler __instance,
			bool __result,
			string? reason)
		{
			if (!__result) return;

			// Always punch the tooltip cache, even if the BreakTimerGameComponent isn't
			// ready yet — otherwise the hover string can lag a state transition by up
			// to TooltipTextCacheTtlSeconds.
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
