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

		static readonly Color IdleColor = Color.white;
		static readonly Color BreakColor = new(0.95f, 0.20f, 0.20f);

		public static void DrawForMood(Pawn? pawn, Rect needRect)
		{
			if (pawn is null || BreakTextures.LightningBolt == null) return;

			Rect iconRect = new(
				needRect.x + SidePadding,
				needRect.y + (needRect.height - IconSize) * 0.5f,
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

			sb.Append("Break: ").AppendLine(state.def.LabelCap.RawText ?? state.def.defName);

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
		/// Builds the tooltip shown when the pawn is not in a mental break. We restrict
		/// the list to the single intensity tier that <em>the pawn's current mood</em>
		/// makes them eligible for — mirroring the game's selector, which only ever rolls
		/// breaks at the highest tier currently below threshold — and we filter to breaks
		/// the worker reports as actually able to fire for this pawn. Each break's
		/// percentage is its <c>Worker.CommonalityFor(pawn, moodCaused: true)</c>
		/// normalised within the chosen tier, matching <c>MentalBreaker</c>'s own math.
		/// </summary>
		static string BuildPossibleBreaksTooltip(Pawn pawn)
		{
			MentalBreaker? breaker = pawn.mindState?.mentalBreaker;
			if (breaker == null)
				return "Possible mental breaks:\n  (none — no mental breaker on this pawn)";

			if (breaker.Blocked)
				return "Possible mental breaks:\n  (none — mental breaks are currently blocked)";
			if (!breaker.CanDoRandomMentalBreaks)
				return "Possible mental breaks:\n  (none — this pawn cannot have random mental breaks)";

			MentalBreakIntensity? eligible = HighestEligibleIntensity(breaker);
			if (eligible is null)
				return "Possible mental breaks:\n  (none — mood is above all break thresholds)";

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
