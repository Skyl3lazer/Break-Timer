using System;
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
		public const float RightPadding = 4f;

		static readonly Color IdleColor = Color.white;
		static readonly Color BreakColor = new(0.95f, 0.20f, 0.20f);

		public static void DrawForMood(Pawn? pawn, Rect needRect)
		{
			if (pawn is null || BreakTextures.LightningBolt == null) return;

			Rect iconRect = new(
				needRect.xMax - IconSize - RightPadding,
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
			if (pawn is null) return "Not Yet Implemented";
			MentalState? state = pawn.MentalState;
			if (state?.def is null)
				return "Not Yet Implemented";

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

		static Vector2 LongLatFor(Pawn pawn)
		{
			PlanetTile tile = pawn.Tile;
			if (tile.Valid && Find.WorldGrid != null)
				return Find.WorldGrid.LongLatOf(tile);
			return Vector2.zero;
		}
	}
}
