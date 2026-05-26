using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BreakTimer.Patches
{
	/// <summary>
	/// Postfix on <see cref="MentalState.RecoverFromState"/> that archives the matching
	/// <see cref="ActiveBreakRecord"/> into the per-pawn history and clears it from the
	/// active map. Fires for both natural recoveries (MTB roll, downed, sleep) and
	/// transition recoveries triggered from <c>TryStartMentalState</c>.
	/// </summary>
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
