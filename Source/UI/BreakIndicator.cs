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

		static readonly MentalBreakIntensity[] IntensityOrder =
		{
			MentalBreakIntensity.Minor,
			MentalBreakIntensity.Major,
			MentalBreakIntensity.Extreme,
		};

		/// <summary>
		/// Builds the tooltip shown when the pawn is not in a mental break: a per-intensity
		/// breakdown of which breaks could fire, with each break's selection chance
		/// computed the same way RimWorld picks them — by normalised
		/// <c>Worker.CommonalityFor(pawn, moodCaused: true)</c> within its intensity tier.
		/// </summary>
		static string BuildPossibleBreaksTooltip(Pawn pawn)
		{
			var groups = new Dictionary<MentalBreakIntensity, List<(BreakInfo info, float weight)>>();

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

			foreach (BreakInfo info in MentalBreakCatalog.All)
			{
				try
				{
					if (info.Intensity == MentalBreakIntensity.None) continue;
					if (info.Requirements.AnomalousBreak && !anomalyActive) continue;
					if (useIdeoFilter && !ideoAllow!.Contains(info.Def)) continue;
					if (!info.CanOccurFor(pawn)) continue;

					float weight = info.CommonalityFor(pawn, moodCaused: true);
					if (weight <= 0f || float.IsNaN(weight) || float.IsInfinity(weight)) continue;

					if (!groups.TryGetValue(info.Intensity, out var bucket))
						groups[info.Intensity] = bucket = new List<(BreakInfo, float)>(8);
					bucket.Add((info, weight));
				}
				catch (Exception ex)
				{
					Log.WarningOnce(
						$"[BreakTimer] Skipping break {info.DefName} in possible-list: {ex.Message}",
						unchecked((int)0xB12D7100 ^ info.DefName.GetHashCode()));
				}
			}

			if (groups.Count == 0)
				return "No mental breaks currently possible for this pawn.";

			var sb = new StringBuilder();
			sb.AppendLine("Possible mental breaks:");

			bool firstSection = true;
			foreach (MentalBreakIntensity intensity in IntensityOrder)
			{
				if (!groups.TryGetValue(intensity, out var list) || list.Count == 0) continue;
				float total = list.Sum(e => e.weight);
				if (total <= 0f) continue;

				if (!firstSection) sb.AppendLine();
				firstSection = false;

				sb.Append(intensity.ToString()).AppendLine(":");
				foreach (var (info, weight) in list.OrderByDescending(e => e.weight))
				{
					float pct = weight / total;
					string label = info.LabelCap;
					if (info.Requirements.AnomalousBreak)
						label += " (anomaly)";
					sb.Append("  ").Append(label).Append(" - ").AppendLine(pct.ToStringPercent("0"));
				}
			}

			return sb.ToString().TrimEnd();
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
