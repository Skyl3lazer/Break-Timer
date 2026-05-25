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
		}
	}
}
