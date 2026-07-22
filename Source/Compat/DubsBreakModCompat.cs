using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BreakTimer
{
    // Optional integration with Dubs Break Mod, which caps the mood-break tier by the single worst grievance.
    public static class DubsBreakModCompat
    {
        static bool resolved;
        static FieldInfo? settingsField;
        static FieldInfo? minorField;
        static FieldInfo? majorField;
        static FieldInfo? extremeField;
        static FieldInfo? colonistsOnlyField;

        public static bool Available
        {
            get
            {
                EnsureResolved();
                return settingsField != null;
            }
        }

        public static void Prime() => EnsureResolved();

        // null means no cap (DBM absent, or its ColonistsOnly leaves the pawn on vanilla); None means capped to no break.
        public static MentalBreakIntensity? GrievanceCap(Pawn pawn)
        {
            EnsureResolved();
            if (settingsField == null || pawn?.needs?.mood?.thoughts == null) return null;

            try
            {
                object? settings = settingsField.GetValue(null);
                if (settings == null) return null;

                if ((bool)colonistsOnlyField!.GetValue(settings) && !pawn.IsColonist) return null;

                int minorLimit = (int)minorField!.GetValue(settings);
                int majorLimit = (int)majorField!.GetValue(settings);
                int extremeLimit = (int)extremeField!.GetValue(settings);

                ThoughtHandler thoughts = pawn.needs.mood.thoughts;
                var groups = new List<Thought>();
                thoughts.GetDistinctMoodThoughtGroups(groups);

                bool minor = false, major = false, extreme = false;
                foreach (Thought group in groups)
                {
                    float offset = thoughts.MoodOffsetOfGroup(group);
                    if (offset <= extremeLimit) extreme = true;
                    else if (offset <= majorLimit) major = true;
                    else if (offset <= minorLimit) minor = true;
                    if (extreme) break;
                }

                return extreme ? MentalBreakIntensity.Extreme
                    : major ? MentalBreakIntensity.Major
                    : minor ? MentalBreakIntensity.Minor
                    : MentalBreakIntensity.None;
            }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[BreakTimer] Dubs Break Mod grievance cap threw: {ex.Message}",
                    Once.Id("dbm-grievance-cap"));
                return null;
            }
        }

        static void EnsureResolved()
        {
            if (resolved) return;
            resolved = true;

            Type? modType = AccessTools.TypeByName("DubsBreakMod.MentalManagementMod");
            Type? settingsType = AccessTools.TypeByName("DubsBreakMod.Settings");
            if (modType == null || settingsType == null) return;

            settingsField = AccessTools.Field(modType, "Settings");
            minorField = AccessTools.Field(settingsType, "MinorLimit");
            majorField = AccessTools.Field(settingsType, "MajorLimit");
            extremeField = AccessTools.Field(settingsType, "ExtremeLimit");
            colonistsOnlyField = AccessTools.Field(settingsType, "ColonistsOnly");

            if (settingsField == null || minorField == null || majorField == null
                || extremeField == null || colonistsOnlyField == null)
                settingsField = null;
        }
    }
}
