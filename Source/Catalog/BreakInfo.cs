using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakTimer
{
	/// <summary>
	/// All cached, derived metadata for a single <see cref="MentalBreakDef"/>: identity,
	/// intensity, commonality, requirements and recovery profile. Designed to be
	/// constructed once at startup and queried freely thereafter.
	/// </summary>
	public sealed class BreakInfo
	{
		public BreakInfo(MentalBreakDef def)
		{
			Def = def ?? throw new ArgumentNullException(nameof(def));
			MentalState = def.mentalState;
			Intensity = def.intensity;
			BaseCommonality = def.baseCommonality;
			CommonalityFactorPerPopulationCurve = def.commonalityFactorPerPopulationCurve;
			WorkerClass = def.workerClass ?? typeof(MentalBreakWorker);

			Requirements = new BreakRequirements(def);
			Duration = MentalState != null ? new BreakDuration(MentalState) : null;

			Category = MentalState?.category ?? MentalStateCategory.Undefined;
			IsAggro = MentalState?.IsAggro ?? false;
			IsExtreme = Intensity == MentalBreakIntensity.Extreme;

			Label = BreakLabels.ForBreak(def);
			LabelCap = BreakLabels.ForBreakCap(def);
		}

		public MentalBreakDef Def { get; }
		public MentalStateDef? MentalState { get; }
		public MentalBreakIntensity Intensity { get; }
		public MentalStateCategory Category { get; }
		public bool IsAggro { get; }
		public bool IsExtreme { get; }
		public float BaseCommonality { get; }
		public SimpleCurve? CommonalityFactorPerPopulationCurve { get; }
		public Type WorkerClass { get; }
		public BreakRequirements Requirements { get; }
		public BreakDuration? Duration { get; }

		public string DefName => Def.defName;

		/// <summary>
		/// Friendly, lowercase label. Resolved once at catalog-build time via
		/// <see cref="BreakLabels.ForBreak"/>, which falls back from
		/// <see cref="MentalBreakDef.label"/> to <see cref="MentalStateDef.label"/> and
		/// finally to the def name, then disambiguates any defs that share a label (e.g.
		/// the two vanilla "insulting spree" breaks become "insulting spree" and
		/// "insulting spree (Targeted)"). Use <see cref="LabelCap"/> for UI display.
		/// </summary>
		public string Label { get; }

		public string LabelCap { get; }

		/// <summary>
		/// Convenience: the actual worker instance the game uses for this break. Wraps the
		/// game's lazy worker accessor so callers can ask <c>BreakCanOccur</c> / <c>CommonalityFor</c>
		/// directly when the strictest answer is needed.
		/// </summary>
		public MentalBreakWorker Worker => Def.Worker;

		/// <summary>
		/// Authoritative "can this break happen to this pawn right now?" check. Delegates
		/// to <see cref="MentalBreakWorker.BreakCanOccur"/>, which covers both the
		/// declarative requirements <see cref="BreakRequirements"/> tracks and any
		/// worker-specific runtime gating (food on hand, exit reachable, ...).
		/// </summary>
		public bool CanOccurFor(Pawn pawn)
		{
			if (pawn is null) return false;
			try { return Worker.BreakCanOccur(pawn); }
			catch (Exception ex)
			{
				Log.WarningOnce(
					$"[BreakTimer] BreakCanOccur threw for {DefName} on {pawn.LabelShort}: {ex.Message}",
					HashCode("CanOccurFor", DefName));
				return false;
			}
		}

		/// <summary>Mirror of <see cref="MentalBreakWorker.CommonalityFor"/> for ranking.</summary>
		public float CommonalityFor(Pawn pawn, bool moodCaused = false)
		{
			if (pawn is null) return 0f;
			try { return Mathf.Max(0f, Worker.CommonalityFor(pawn, moodCaused)); }
			catch (Exception ex)
			{
				Log.WarningOnce(
					$"[BreakTimer] CommonalityFor threw for {DefName} on {pawn.LabelShort}: {ex.Message}",
					HashCode("CommonalityFor", DefName));
				return 0f;
			}
		}

		/// <summary>
		/// All requirement reasons (declarative + worker bridge) that would block this
		/// break for <paramref name="pawn"/>. Returns an empty sequence when the break is
		/// fully eligible.
		/// </summary>
		public IEnumerable<string> GetUnmetReasons(Pawn pawn)
		{
			if (pawn is null) yield break;

			foreach (string r in Requirements.GetUnmetReasons(pawn))
				yield return r;

			if (Requirements.DeclarativelyAllowsPawn(pawn) && !CanOccurFor(pawn))
				yield return "Worker-specific prerequisites not met";
		}

		public override string ToString()
			=> $"BreakInfo({DefName}, {Intensity}, {(MentalState?.defName ?? "no state")})";

		static int HashCode(string kind, string defName)
			=> unchecked(kind.GetHashCode() * 397 ^ defName.GetHashCode());
	}
}
