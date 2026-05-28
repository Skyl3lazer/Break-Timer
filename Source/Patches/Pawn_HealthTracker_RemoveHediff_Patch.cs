using System;
using HarmonyLib;
using Verse;

namespace BreakTimer.Patches
{
	/// <summary>
	/// Catatonic ends when its CatatonicBreakdown hediff is removed (its disappears timer
	/// elapses), not via <see cref="Verse.AI.MentalState.RecoverFromState"/>. Archive the
	/// active record to history here. Fires on every hediff removal but is gated by a
	/// single def compare, so non-catatonic removals cost almost nothing.
	/// </summary>
	[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
	public static class Pawn_HealthTracker_RemoveHediff_Patch
	{
		static void Postfix(Hediff hediff)
		{
			if (hediff?.def is null || hediff.def != CatatonicBreak.HediffDef) return;

			Pawn? pawn = hediff.pawn;
			if (pawn is null) return;

			BreakIndicator.InvalidateTooltipCache();

			BreakTimerGameComponent? store = BreakTimerGameComponent.Instance;
			if (store is null) return;

			try
			{
				store.OnHediffBreakEnded(pawn, CatatonicBreak.BreakDef);
			}
			catch (Exception ex)
			{
				Log.Error($"[BreakTimer] Catatonic RemoveHediff postfix failed: {ex}");
			}
		}
	}
}
