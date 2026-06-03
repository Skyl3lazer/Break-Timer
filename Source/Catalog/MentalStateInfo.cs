using System;
using Verse;

namespace BreakTimer
{
	// Duration + label for any MentalStateDef, including states with no MentalBreakDef
	// (e.g. WanderConfused from Dementia). Broader, cheaper counterpart to BreakInfo.
	public sealed class MentalStateInfo
	{
		public MentalStateInfo(MentalStateDef def)
		{
			Def = def ?? throw new ArgumentNullException(nameof(def));
			Duration = new BreakDuration(def);
			LabelCap = BreakLabels.ForState(def);
		}

		public MentalStateDef Def { get; }
		public BreakDuration Duration { get; }
		public string DefName => Def.defName;

		// Raw label; collision disambiguation is applied contextually at render time.
		public string LabelCap { get; }
	}
}
