using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BreakTimer.Patches
{
    // Lifts vanilla's 260f tooltip-width cap for our expanded view only, keyed by tooltip id.
    [HarmonyPatch(typeof(ActiveTip), "TipRect", MethodType.Getter)]
    public static class ActiveTip_TipRect_Patch
    {
        static void Postfix(ActiveTip __instance, ref Rect __result)
        {
            if (BreakIndicator.WideTooltipId == 0 || __instance.signal.uniqueId != BreakIndicator.WideTooltipId)
                return;

            try
            {
                string? text = __instance.signal.textGetter != null
                    ? __instance.signal.textGetter()
                    : __instance.signal.text;
                if (text.NullOrEmpty()) return;
                text = text.TrimEnd();

                Text.Font = GameFont.Small;
                Vector2 size = Text.CalcSize(text);
                if (size.x > BreakIndicator.ExpandedMaxWidth)
                {
                    size.x = BreakIndicator.ExpandedMaxWidth;
                    size.y = Text.CalcHeight(text, size.x);
                }
                __result = new Rect(0f, 0f, size.x, size.y).ContractedBy(-4f).RoundedCeil();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[BreakTimer] ActiveTip.TipRect postfix failed: {ex.Message}", Once.Id("tiprect"));
            }
        }
    }
}
