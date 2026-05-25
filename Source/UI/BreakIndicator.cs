using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakTimer
{
	/// <summary>
	/// Renders the small lightning-bolt indicator that lives at the right edge of the
	/// Mood need bar on the Needs tab, plus the hover tooltip describing the pawn's
	/// active mental break (or a placeholder when there isn't one).
	/// </summary>
	public static class BreakIndicator
	{
		public const float IconSize = 22f;
		public const float SidePadding = 4f;

		// Mirrors Need.DrawOnGUI's internal layout: bar sits in the lower half of the
		// rect, leaving the top half for the "Mood" label, with `num2 = 14f` bottom
		// padding inside the bar area.
		const float NeedLabelHeightFraction = 0.5f;
		const float NeedBottomMargin = 14f;

		static readonly Color IdleColor = Color.white;
		static readonly Color BreakColor = new(0.95f, 0.20f, 0.20f);

		public static void DrawForMood(Pawn? pawn, Rect needRect)
		{
			if (pawn is null || BreakTextures.LightningBolt == null) return;

			float barAreaY = needRect.y + needRect.height * NeedLabelHeightFraction;
			float barAreaHeight = needRect.height * (1f - NeedLabelHeightFraction) - NeedBottomMargin;
			if (barAreaHeight < IconSize) barAreaHeight = IconSize;

			Rect iconRect = new(
				needRect.x + SidePadding,
				barAreaY + (barAreaHeight - IconSize) * 0.5f,
				IconSize,
				IconSize);

			bool inBreak = pawn.MentalState != null;

			Color prev = GUI.color;
			GUI.color = inBreak ? BreakColor : IdleColor;
			GUI.DrawTexture(iconRect, BreakTextures.LightningBolt);
			GUI.color = prev;

			if (Mouse.IsOver(iconRect))
			{
				Widgets.DrawHighlight(iconRect);
				int tooltipId = HashTooltipId(pawn);
				Func<string> getter = () => BuildTooltip(pawn);
				TooltipHandler.TipRegion(iconRect, new TipSignal(getter, tooltipId));
			}
		}

		static int HashTooltipId(Pawn pawn) => unchecked(0x4B524554 ^ pawn.thingIDNumber);

		static string BuildTooltip(Pawn? pawn)
		{
			if (pawn is null) return "(no pawn selected)";

			try
			{
				MentalState? state = pawn.MentalState;
				return state?.def is null
					? BuildPossibleBreaksTooltip(pawn)
					: BuildActiveBreakTooltip(pawn, state);
			}
			catch (Exception ex)
			{
				Log.ErrorOnce(
					$"[BreakTimer] Tooltip build failed for {pawn.LabelShort}: {ex}",
					unchecked((int)0xB12D7001));
				return "Break Timer: error building tooltip (see log).";
			}
		}

		static string BuildActiveBreakTooltip(Pawn pawn, MentalState state)
		{
			var sb = new StringBuilder();

			sb.Append("Break: ").AppendLine(BreakLabels.ForState(state.def));

			ActiveBreakRecord? rec = BreakTimerGameComponent.Instance?.GetActive(pawn);
			int nowTick = Find.TickManager.TicksGame;
			int startTick = rec?.startTick ?? (nowTick - Mathf.Max(0, state.Age));

			Vector2 longLat = LongLatFor(pawn);
			long absStartTick = ToAbsTick(startTick);
			sb.Append("Started: ").AppendLine(GenDate.DateFullStringWithHourAt(absStartTick, longLat));

			BreakInfo? info = MentalBreakCatalog.GetForState(state.def);
			if (info?.Duration != null)
			{
				BreakDurationRemaining remaining = info.Duration.GetRemaining(state);
				long minEnd = ToAbsTick(nowTick + remaining.MinTicks);
				long maxEnd = ToAbsTick(nowTick + remaining.MaxTicks);

				int minHours = TicksToHoursCeil(remaining.MinTicks);
				int maxHours = TicksToHoursCeil(remaining.MaxTicks);
				if (info.Duration.HasUnboundedMax)
					sb.Append("Remaining: ").Append(minHours).AppendLine("h+");
				else if (minHours == maxHours)
					sb.Append("Remaining: ").Append(minHours).AppendLine("h");
				else
					sb.Append("Remaining: ").Append(minHours).Append("h - ").Append(maxHours).AppendLine("h");

				if (info.Duration.HasUnboundedMax)
				{
					sb.Append("Ends after: ")
						.Append(GenDate.DateFullStringWithHourAt(minEnd, longLat))
						.AppendLine(" (no fixed end)");
				}
				else if (remaining.MinTicks == remaining.MaxTicks)
				{
					sb.Append("Ends: ").AppendLine(GenDate.DateFullStringWithHourAt(maxEnd, longLat));
				}
				else
				{
					sb.Append("Ends between: ")
						.Append(GenDate.DateFullStringWithHourAt(minEnd, longLat))
						.Append("  -  ")
						.AppendLine(GenDate.DateFullStringWithHourAt(maxEnd, longLat));
				}
			}

			string description = ExtractDescription(state, pawn);
			if (!description.NullOrEmpty())
			{
				sb.AppendLine();
				sb.Append(description);
			}

			return sb.ToString().TrimEnd();
		}

		/// <summary>
		/// Builds the tooltip shown when the pawn is not in a mental break. Combines:
		/// (a) the mood-driven section — the single intensity tier the pawn's
		/// <c>CurMood</c> currently qualifies them for, with selection chances normalised
		/// the same way <c>MentalBreaker</c> rolls them; and (b) an "other potential
		/// states" section listing mood-independent triggers (Pyromaniac-style trait
		/// <see cref="TraitMentalStateGiver"/> entries and baby <see cref="MentalFitDef"/>
		/// fits), each annotated with its source and MTB.
		/// </summary>
		static string BuildPossibleBreaksTooltip(Pawn pawn)
		{
			string moodSection = BuildMoodTierSection(pawn);
			string otherSection = BuildOtherTriggersSection(pawn);

			if (moodSection.Length == 0 && otherSection.Length == 0)
				return "Possible mental breaks:\n  (none — mood is above all break thresholds)";

			if (moodSection.Length > 0 && otherSection.Length > 0)
				return moodSection + "\n\n" + otherSection;

			return moodSection.Length > 0 ? moodSection : otherSection;
		}

		static string BuildMoodTierSection(Pawn pawn)
		{
			MentalBreaker? breaker = pawn.mindState?.mentalBreaker;
			if (breaker == null) return string.Empty;
			if (breaker.Blocked) return "Possible mental breaks:\n  (none — mental breaks are currently blocked)";
			if (!breaker.CanDoRandomMentalBreaks) return string.Empty;

			MentalBreakIntensity? eligible = HighestEligibleIntensity(breaker);
			if (eligible is null) return string.Empty;

			var entries = new List<(BreakInfo info, float weight)>(8);

			HashSet<MentalBreakDef>? ideoAllow = null;
			try { ideoAllow = pawn.Ideo?.cachedPossibleMentalBreaks; }
			catch (Exception ex)
			{
				Log.WarningOnce(
					$"[BreakTimer] Ideo.cachedPossibleMentalBreaks threw: {ex.Message}",
					unchecked((int)0xB12D7002));
			}
			bool useIdeoFilter = ideoAllow != null && ideoAllow.Count > 0;
			bool anomalyActive = ModsConfig.AnomalyActive;

			foreach (BreakInfo info in MentalBreakCatalog.OfIntensity(eligible.Value))
			{
				try
				{
					if (info.Requirements.AnomalousBreak && !anomalyActive) continue;
					if (useIdeoFilter && !ideoAllow!.Contains(info.Def)) continue;
					if (!info.CanOccurFor(pawn)) continue;

					float weight = info.CommonalityFor(pawn, moodCaused: true);
					if (weight <= 0f || float.IsNaN(weight) || float.IsInfinity(weight)) continue;

					entries.Add((info, weight));
				}
				catch (Exception ex)
				{
					Log.WarningOnce(
						$"[BreakTimer] Skipping break {info.DefName} in possible-list: {ex.Message}",
						unchecked((int)0xB12D7100 ^ info.DefName.GetHashCode()));
				}
			}

			if (entries.Count == 0)
				return $"Possible mental breaks ({eligible.Value}):\n  (none currently eligible)";

			float total = entries.Sum(e => e.weight);
			var sb = new StringBuilder();
			sb.Append("Possible mental breaks (").Append(eligible.Value).AppendLine("):");
			foreach (var (info, weight) in entries.OrderByDescending(e => e.weight))
			{
				float pct = total > 0f ? weight / total : 0f;
				string label = info.LabelCap;
				if (info.Requirements.AnomalousBreak) label += " (anomaly)";
				sb.Append("  ").Append(label).Append(" - ").AppendLine(pct.ToStringPercent("0"));
			}

			return sb.ToString().TrimEnd();
		}

		readonly struct ExtraTrigger
		{
			public ExtraTrigger(string label, string source, float mtbDays)
			{
				Label = label;
				Source = source;
				MtbDays = mtbDays;
			}

			public string Label { get; }
			public string Source { get; }
			public float MtbDays { get; }
		}

		static string BuildOtherTriggersSection(Pawn pawn)
		{
			var extras = new List<ExtraTrigger>(4);
			CollectTraitTriggers(pawn, extras);
			CollectMentalFitTriggers(pawn, extras);
			if (extras.Count == 0) return string.Empty;

			extras.Sort((a, b) => a.MtbDays.CompareTo(b.MtbDays));

			var sb = new StringBuilder();
			sb.AppendLine("Other potential states:");
			foreach (ExtraTrigger e in extras)
			{
				sb.Append("  ")
					.Append(e.Label)
					.Append(" (")
					.Append(e.Source)
					.Append(") - every ~")
					.AppendLine(FormatMtb(e.MtbDays));
			}
			return sb.ToString().TrimEnd();
		}

		static void CollectTraitTriggers(Pawn pawn, List<ExtraTrigger> sink)
		{
			TraitSet? traits = pawn.story?.traits;
			if (traits == null) return;

			foreach (Trait trait in traits.allTraits)
			{
				try
				{
					if (trait.Suppressed) continue;
					TraitDegreeData data = trait.CurrentData;
					if (data == null) continue;

					string sourceTag = BreakLabels.TraitSourceTag(trait);

					if (data.forcedMentalState != null && data.forcedMentalStateMtbDays > 0f)
					{
						MentalStateDef state = data.forcedMentalState;
						if (state.Worker == null || state.Worker.StateCanOccur(pawn))
							sink.Add(new ExtraTrigger(
								BreakLabels.ForState(state),
								sourceTag,
								data.forcedMentalStateMtbDays));
					}

					if (data.randomMentalState != null && data.randomMentalStateMtbDaysMoodCurve != null)
					{
						MentalStateDef state = data.randomMentalState;
						float mood = pawn.needs?.mood?.CurLevelPercentage ?? 1f;
						float mtb = data.randomMentalStateMtbDaysMoodCurve.Evaluate(mood);
						if (mtb > 0f && !float.IsInfinity(mtb) && !float.IsNaN(mtb)
							&& (state.Worker == null || state.Worker.StateCanOccur(pawn)))
						{
							sink.Add(new ExtraTrigger(
								BreakLabels.ForState(state),
								sourceTag,
								mtb));
						}
					}
				}
				catch (Exception ex)
				{
					Log.WarningOnce(
						$"[BreakTimer] Trait giver scan failed for {trait?.def?.defName ?? "?"}: {ex.Message}",
						unchecked((int)0xB12D7200 ^ (trait?.def?.defName?.GetHashCode() ?? 0)));
				}
			}
		}

		static void CollectMentalFitTriggers(Pawn pawn, List<ExtraTrigger> sink)
		{
			List<MentalFitDef> fits = DefDatabase<MentalFitDef>.AllDefsListForReading;
			if (fits == null || fits.Count == 0) return;

			string stage = pawn.DevelopmentalStage.ToString().ToLowerInvariant();

			foreach (MentalFitDef fit in fits)
			{
				try
				{
					float mtb = fit.CalculateMTBDays(pawn);
					if (mtb <= 0f || float.IsInfinity(mtb) || float.IsNaN(mtb)) continue;

					MentalStateDef? state = fit.mentalState;
					if (state?.Worker != null && !state.Worker.StateCanOccur(pawn)) continue;

					string label = state != null ? BreakLabels.ForState(state) : BreakLabels.ForFit(fit);
					sink.Add(new ExtraTrigger(label, stage, mtb));
				}
				catch (Exception ex)
				{
					Log.WarningOnce(
						$"[BreakTimer] MentalFit scan failed for {fit?.defName ?? "?"}: {ex.Message}",
						unchecked((int)0xB12D7300 ^ (fit?.defName?.GetHashCode() ?? 0)));
				}
			}
		}

		static string FormatMtb(float mtbDays)
		{
			if (mtbDays >= 1f) return Mathf.RoundToInt(mtbDays).ToString() + "d";
			int hours = Mathf.Max(1, Mathf.CeilToInt(mtbDays * 24f));
			return hours.ToString() + "h";
		}

		/// <summary>
		/// Returns the highest <see cref="MentalBreakIntensity"/> the pawn currently
		/// qualifies for purely off <c>CurMood</c> vs the per-tier thresholds. Mirrors
		/// <c>MentalBreaker.CurrentDesiredMoodBreakIntensity</c>'s tiering, minus the
		/// 2000-tick dwell requirement (which is too coarse for a snapshot tooltip).
		/// </summary>
		static MentalBreakIntensity? HighestEligibleIntensity(MentalBreaker breaker)
		{
			float mood = breaker.CurMood;
			if (mood < breaker.BreakThresholdExtreme) return MentalBreakIntensity.Extreme;
			if (mood < breaker.BreakThresholdMajor) return MentalBreakIntensity.Major;
			if (mood < breaker.BreakThresholdMinor) return MentalBreakIntensity.Minor;
			return null;
		}

		static string ExtractDescription(MentalState state, Pawn pawn)
		{
			TaggedString letter = state.GetBeginLetterText();
			if (!letter.NullOrEmpty())
				return letter.Resolve();

			if (!state.def.baseInspectLine.NullOrEmpty())
				return state.def.baseInspectLine.Formatted(pawn.LabelShort, pawn.Named("PAWN"))
					.AdjustedFor(pawn)
					.Resolve();

			return string.Empty;
		}

		static long ToAbsTick(int gameTick)
		{
			long offset = GenTicks.TicksAbs - Find.TickManager.TicksGame;
			return offset + gameTick;
		}

		static int TicksToHoursCeil(int ticks)
		{
			if (ticks <= 0) return 0;
			return Mathf.CeilToInt(ticks / (float)GenDate.TicksPerHour);
		}

		static Vector2 LongLatFor(Pawn pawn)
		{
			PlanetTile tile = pawn.Tile;
			if (tile.Valid && Find.WorldGrid != null)
				return Find.WorldGrid.LongLatOf(tile);
			return Vector2.zero;
		}
	}
}
