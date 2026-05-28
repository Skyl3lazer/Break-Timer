using Verse;
using Verse.AI;

namespace BreakTimer
{
	/// <summary>
	/// Shared lookups for the Catatonic break, which is hediff-driven (no MentalState):
	/// its worker applies the CatatonicBreakdown hediff, whose HediffComp_Disappears
	/// timer carries the remaining duration. Centralised so the indicator and the
	/// start/end patches agree on detection and time-remaining math.
	/// </summary>
	public static class CatatonicBreak
	{
		public const string HediffDefName = "CatatonicBreakdown";
		public const string BreakDefName = "Catatonic";

		static bool resolved;
		static HediffDef? hediffDef;

		public static HediffDef? HediffDef
		{
			get
			{
				if (!resolved)
				{
					hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(HediffDefName);
					resolved = true;
				}
				return hediffDef;
			}
		}

		public static MentalBreakDef? BreakDef => MentalBreakCatalog.Get(BreakDefName)?.Def;

		/// <summary>The active CatatonicBreakdown hediff on the pawn, or null.</summary>
		public static Hediff? FindOn(Pawn? pawn)
		{
			HediffDef? def = HediffDef;
			if (def is null || pawn?.health?.hediffSet is null) return null;
			return pawn.health.hediffSet.GetFirstHediffOfDef(def);
		}

		/// <summary>
		/// Ticks until the breakdown lifts, taken from the hediff's disappears comp. Uses
		/// EffectiveTicksToDisappear so any TicksLostPerTick scaling is honoured. Returns
		/// 0 when no timed comp is present.
		/// </summary>
		public static int RemainingTicks(Hediff hediff)
		{
			HediffComp_Disappears? comp = (hediff as HediffWithComps)?.TryGetComp<HediffComp_Disappears>();
			return comp?.EffectiveTicksToDisappear ?? 0;
		}
	}
}
