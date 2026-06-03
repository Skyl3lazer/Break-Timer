using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakTimer
{
    // The Catatonic break is hediff-driven: it has no MentalState, its worker applies the
    // CatatonicBreakdown hediff, and that hediff's HediffComp_Disappears timer carries the
    // remaining duration. Centralised so the indicator and start/end patches agree.
    public static class CatatonicBreak
    {
        // The active CatatonicBreakdown hediff on the pawn, or null.
        public static Hediff? FindOn(Pawn? pawn)
        {
            HediffDef? def = BreakTimerDefOf.CatatonicBreakdown;
            if (def is null || pawn?.health?.hediffSet is null) return null;
            return pawn.health.hediffSet.GetFirstHediffOfDef(def);
        }

        // Remaining as a min/max window, not the exact countdown: the breakdown lasts a value
        // rolled from disappearsAfterTicks (e.g. 100k-300k), and revealing the rolled total
        // would be unfair, so we report the configured range minus elapsed. Zero window if no
        // timed comp is present.
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
