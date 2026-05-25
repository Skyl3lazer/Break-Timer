using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BreakTimer.Patches
{
	/// <summary>
	/// Postfix on <see cref="Need.DrawOnGUI"/> that paints the Break Timer indicator on
	/// top of the right edge of the Mood need bar. Filtered to the Mood def so other
	/// need bars (food, rest, ...) aren't touched.
	/// </summary>
	[HarmonyPatch(typeof(Need), nameof(Need.DrawOnGUI))]
	public static class Need_DrawOnGUI_Patch
	{
		const string MoodDefName = "Mood";

		static readonly AccessTools.FieldRef<Need, Pawn> pawnField =
			AccessTools.FieldRefAccess<Need, Pawn>("pawn");

		static void Postfix(Need __instance, Rect rect)
		{
			if (__instance?.def is null || __instance.def.defName != MoodDefName) return;
			if (rect.width < BreakIndicator.IconSize * 2f) return;

			try
			{
				Pawn pawn = pawnField(__instance);
				BreakIndicator.DrawForMood(pawn, rect);
			}
			catch (Exception ex)
			{
				Log.ErrorOnce(
					$"[BreakTimer] Need.DrawOnGUI postfix failed: {ex.Message}",
					unchecked((int)0xB12D31C9));
			}
		}
	}
}
