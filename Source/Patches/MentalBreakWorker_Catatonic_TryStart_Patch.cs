using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BreakTimer.Patches
{
	// Catatonic has no MentalState (its worker applies the CatatonicBreakdown hediff), so the
	// MentalState start patch never sees it. Record the start tick from the worker instead.
	[HarmonyPatch(typeof(MentalBreakWorker_Catatonic), nameof(MentalBreakWorker.TryStart))]
	public static class MentalBreakWorker_Catatonic_TryStart_Patch
	{
		static void Postfix(Pawn pawn, string? reason, bool causedByMood, bool __result)
		{
			if (!__result || pawn is null) return;

			BreakIndicator.InvalidateTooltipCache();

			BreakTimerGameComponent? store = BreakTimerGameComponent.Instance;
			if (store is null) return;

			try
			{
				store.OnHediffBreakStarted(pawn, CatatonicBreak.BreakDef, reason, causedByMood);
			}
			catch (Exception ex)
			{
				Log.Error($"[BreakTimer] Catatonic TryStart postfix failed: {ex}");
			}
		}
	}
}
