using UnityEngine;
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
		/// Remaining duration as a min/max window rather than the exact countdown. The
		/// breakdown lasts a value rolled from <c>disappearsAfterTicks</c> (e.g. 100k–300k);
		/// revealing the rolled total would be unfair, so we report the configured range
		/// minus elapsed time, mirroring how mood breaks show a min/max remaining. Returns
		/// a zero window when no timed comp is present.
		/// </summary>
		public static BreakDurationRemaining GetRemaining(Hediff hediff)
		{
			HediffComp_Disappears? comp = (hediff as HediffWithComps)?.TryGetComp<HediffComp_Disappears>();
			if (comp is null) return new BreakDurationRemaining(0, 0, 0);

			IntRange range = comp.Props.disappearsAfterTicks;
			int elapsed = Mathf.Max(0, hediff.ageTicks);
			int minRemaining = Mathf.Max(0, range.min - elapsed);
			int maxRemaining = Mathf.Max(0, range.max - elapsed);
			int expectedRemaining = Mathf.Max(0, (range.min + range.max) / 2 - elapsed);
			return new BreakDurationRemaining(minRemaining, expectedRemaining, maxRemaining);
		}
	}
}
