using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace BreakTimer
{
    [StaticConstructorOnStartup]
    public static class BreakTimerMod
    {
        public static readonly Harmony Harmony = new("local.BreakTimer");

        static BreakTimerMod()
        {
            MentalBreakCatalog.EnsureBuilt();

            try
            {
                Harmony.PatchAll(Assembly.GetExecutingAssembly());
                if (Prefs.DevMode)
                    Log.Message($"[BreakTimer] Harmony patches applied: {Harmony.GetPatchedMethods().Count()} methods.");
            }
            catch (Exception ex)
            {
                Log.Error($"[BreakTimer] Harmony PatchAll failed: {ex}");
            }
        }
    }
}
