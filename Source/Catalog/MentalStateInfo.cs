using System;
using Verse;

namespace BreakTimer
{
	/// <summary>
	/// Catalog entry for any <see cref="MentalStateDef"/>, whether or not it has a
	/// corresponding <see cref="MentalBreakDef"/>. Used to surface duration info for
	/// states that the mood-roll path doesn't own — hediff <c>mentalStateGivers</c>
	/// (Alzheimer's / Dementia / etc. → <c>WanderConfused</c>), trait givers, mental-fit
	/// givers, scripted incidents, and so on.
	/// </summary>
	/// <remarks>
	/// <see cref="BreakInfo"/> covers the same ground for mood-driven breaks but also
	/// carries break-specific metadata (intensity, commonality, worker class). When all
	/// the caller wants is "how long does this state last and what is it called?",
	/// <see cref="MentalStateInfo"/> is the cheaper, broader entry point.
	/// </remarks>
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

		/// <summary>
		/// Capitalised friendly label. Resolved via <see cref="BreakLabels.ForState"/>,
		/// which falls back from <see cref="MentalStateDef.label"/> to the def name.
		/// Collision disambiguation is contextual and applied by the tooltip renderer.
		/// </summary>
		public string LabelCap { get; }
	}
}
