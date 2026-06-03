using RimWorld;
using Verse;

namespace BreakTimer
{
    // The Catatonic MentalBreakDef has no MentalState; its worker applies the
    // CatatonicBreakdown hediff instead. Both are vanilla (Core) defs.
    [DefOf]
    public static class BreakTimerDefOf
    {
        public static MentalBreakDef? Catatonic;
        public static HediffDef? CatatonicBreakdown;
    }
}
