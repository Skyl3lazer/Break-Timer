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
    // Renders the lightning-bolt indicator at the right edge of the Mood need bar, plus the
    // hover tooltip describing the pawn's active break (or possible breaks when there's none).
    public static class BreakIndicator
    {
        public const float IconSize = 22f;
        public const float SidePadding = 4f;

        // Mirrors Need.DrawOnGUI's layout: the bar sits in the lower half of the rect (top
        // half is the "Mood" label), with 14f bottom padding inside the bar area.
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

            bool inBreak = pawn.MentalState != null || CatatonicBreak.FindOn(pawn) != null;

            Color prev = GUI.color;
            GUI.color = inBreak ? BreakColor : IdleColor;
            GUI.DrawTexture(iconRect, BreakTextures.LightningBolt);
            GUI.color = prev;

            if (Mouse.IsOver(iconRect))
            {
                Widgets.DrawHighlight(iconRect);
                int tooltipId = HashTooltipId(pawn);
                Func<string> getter = () => GetCachedTooltip(pawn);
                TooltipHandler.TipRegion(iconRect, new TipSignal(getter, tooltipId));
            }
        }

        static int HashTooltipId(Pawn pawn) => unchecked(0x4B524554 ^ pawn.thingIDNumber);

        // Cache the rendered string ~1s: TipSignal re-invokes the getter every frame, and the
        // build pipeline makes reflective worker/MTB calls. None of the inputs change that
        // fast (mood ticks ~every 60, "Remaining" rounds to hours). Patches invalidate on
        // state change so transitions stay instant.
        const float TooltipTextCacheTtlSeconds = 1.0f;

        static int cachedPawnId;
        static MentalStateDef? cachedActiveStateDef;
        static HediffDef? cachedActiveBreakHediffDef;
        static float cachedAtRealtime;
        static string? cachedTooltipText;

        static string GetCachedTooltip(Pawn? pawn)
        {
            if (pawn is null) return "BreakTimer.NoPawnSelected".Translate();

            int id = pawn.thingIDNumber;
            MentalStateDef? activeStateDef = pawn.MentalState?.def;
            HediffDef? activeBreakHediffDef = CatatonicBreak.FindOn(pawn)?.def;
            float now = Time.realtimeSinceStartup;

            if (cachedTooltipText != null
                && cachedPawnId == id
                && ReferenceEquals(cachedActiveStateDef, activeStateDef)
                && ReferenceEquals(cachedActiveBreakHediffDef, activeBreakHediffDef)
                && now - cachedAtRealtime < TooltipTextCacheTtlSeconds)
            {
                return cachedTooltipText;
            }

            string text = BuildTooltip(pawn);
            cachedPawnId = id;
            cachedActiveStateDef = activeStateDef;
            cachedActiveBreakHediffDef = activeBreakHediffDef;
            cachedAtRealtime = now;
            cachedTooltipText = text;
            return text;
        }

        // Called from the start/end patches so a transition shows on the next frame instead of
        // waiting out the TTL.
        public static void InvalidateTooltipCache()
        {
            cachedTooltipText = null;
        }

        static string BuildTooltip(Pawn? pawn)
        {
            if (pawn is null) return "BreakTimer.NoPawnSelected".Translate();

            try
            {
                MentalState? state = pawn.MentalState;
                if (state?.def != null)
                    return BuildActiveBreakTooltip(pawn, state);

                Hediff? breakHediff = CatatonicBreak.FindOn(pawn);
                return breakHediff != null
                    ? BuildHediffBreakTooltip(pawn, breakHediff)
                    : BuildPossibleBreaksTooltip(pawn);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"[BreakTimer] Tooltip build failed for {pawn.LabelShort}: {ex}",
                    Once.Id("tooltip-build"));
                return "BreakTimer.TooltipError".Translate();
            }
        }

        static string BuildActiveBreakTooltip(Pawn pawn, MentalState state)
        {
            var sb = new StringBuilder();

            sb.AppendLine("BreakTimer.Break".Translate(BreakLabels.ForState(state.def)));

            ActiveBreakRecord? rec = BreakTimerGameComponent.Instance?.GetActive(pawn);
            int nowTick = Find.TickManager.TicksGame;
            int startTick = rec?.startTick ?? (nowTick - Mathf.Max(0, state.Age));

            Vector2 longLat = LongLatFor(pawn);
            long absStartTick = ToAbsTick(startTick);
            AppendDateLine(sb, "BreakTimer.Started".Translate(),absStartTick, longLat);

            // Mood-driven breaks resolve through BreakInfo; everything else (hediff/trait
            // givers like WanderConfused) falls back to MentalStateInfo for a duration.
            BreakDuration? duration =
                MentalBreakCatalog.GetForState(state.def)?.Duration
                ?? MentalBreakCatalog.GetStateInfo(state.def)?.Duration;

            if (duration != null)
            {
                BreakDurationRemaining remaining = duration.GetRemaining(state);
                AppendRemainingAndEnds(sb, remaining.MinTicks, remaining.MaxTicks, duration.HasUnboundedMax, nowTick, longLat);
            }

            string description = ExtractDescription(state, pawn);
            if (!description.NullOrEmpty())
            {
                sb.AppendLine();
                sb.Append(description);
            }

            return sb.ToString().TrimEnd();
        }

        // Shared "Remaining/Ends" renderer for any break with a min/max recovery window, so
        // mood breaks and the catatonic hediff break read the same way.
        static void AppendRemainingAndEnds(StringBuilder sb, int minTicks, int maxTicks, bool unboundedMax, int nowTick, Vector2 longLat)
        {
            long minEnd = ToAbsTick(nowTick + minTicks);
            long maxEnd = ToAbsTick(nowTick + maxTicks);

            int minHours = TicksToHoursCeil(minTicks);
            int maxHours = TicksToHoursCeil(maxTicks);

            if (unboundedMax)
                sb.AppendLine("BreakTimer.RemainingPlus".Translate(minHours));
            else if (minHours == maxHours)
                sb.AppendLine("BreakTimer.Remaining".Translate(minHours));
            else
                sb.AppendLine("BreakTimer.RemainingRange".Translate(minHours, maxHours));

            if (unboundedMax)
            {
                sb.AppendLine("BreakTimer.EndsAfter".Translate());
                sb.Append("  ").AppendLine(GenDate.DateFullStringWithHourAt(minEnd, longLat));
                sb.Append("  ").AppendLine("BreakTimer.NoFixedEnd".Translate());
            }
            else if (minTicks == maxTicks)
            {
                AppendDateLine(sb, "BreakTimer.Ends".Translate(), maxEnd, longLat);
            }
            else
            {
                sb.AppendLine("BreakTimer.EndsBetween".Translate());
                sb.Append("  ").AppendLine("BreakTimer.Earliest".Translate(GenDate.DateFullStringWithHourAt(minEnd, longLat)));
                sb.Append("  ").AppendLine("BreakTimer.Latest".Translate(GenDate.DateFullStringWithHourAt(maxEnd, longLat)));
            }
        }

        // Catatonic has no mentalState — its worker applies the CatatonicBreakdown hediff, so
        // pawn.MentalState is null. Detection and timing live on CatatonicBreak.
        static string BuildHediffBreakTooltip(Pawn pawn, Hediff hediff)
        {
            var sb = new StringBuilder();

            string label = BreakTimerDefOf.Catatonic != null
                ? BreakLabels.ForBreakCap(BreakTimerDefOf.Catatonic)
                : hediff.LabelCap;
            sb.AppendLine("BreakTimer.Break".Translate(label));

            int nowTick = Find.TickManager.TicksGame;
            ActiveBreakRecord? rec = BreakTimerGameComponent.Instance?.GetActive(pawn);
            int startTick = rec?.startTick ?? (nowTick - Mathf.Max(0, hediff.ageTicks));
            Vector2 longLat = LongLatFor(pawn);
            AppendDateLine(sb, "BreakTimer.Started".Translate(),ToAbsTick(startTick), longLat);

            BreakDurationRemaining remaining = CatatonicBreak.GetRemaining(hediff);
            if (remaining.MaxTicks > 0)
                AppendRemainingAndEnds(sb, remaining.MinTicks, remaining.MaxTicks, unboundedMax: false, nowTick, longLat);

            string? description = hediff.def?.description;
            if (!description.NullOrEmpty())
            {
                sb.AppendLine();
                sb.Append(description);
            }

            return sb.ToString().TrimEnd();
        }

        // Tooltip when the pawn is not in a break. Two sections: the mood-driven tier (the
        // single intensity CurMood qualifies for, weighted the way MentalBreaker rolls them)
        // and "other potential states" (mood-independent trait and mental-fit triggers, each
        // with source and MTB). Runs in three passes — gather, disambiguate across the union
        // of both sections, render — so a defName collision only gets a tag when both
        // colliding items are actually shown.
        static string BuildPossibleBreaksTooltip(Pawn pawn)
        {
            MoodTier mood = CollectMoodTier(pawn);
            var others = new List<ExtraTrigger>(4);
            CollectTraitTriggers(pawn, others);
            CollectMentalFitTriggers(pawn, others);
            CollectHediffTriggers(pawn, others);
            others.Sort((a, b) => a.MtbDays.CompareTo(b.MtbDays));

            int moodCount = mood.Entries?.Count ?? 0;
            int otherCount = others.Count;

            var pool = new List<(string label, string defName)>(moodCount + otherCount);
            if (mood.Entries != null)
            {
                foreach (var (info, _) in mood.Entries)
                    pool.Add((info.LabelCap, info.DefName));
            }
            foreach (ExtraTrigger e in others)
                pool.Add((e.Label, e.DefName));

            List<string> labels = LabelDisambiguator.Resolve(pool);

            string moodSection = RenderMoodTier(mood, labels, 0);
            string otherSection = RenderOtherTriggers(others, labels, moodCount);

            if (moodSection.Length == 0 && otherSection.Length == 0)
                return "BreakTimer.PossibleBreaksNonePossible".Translate();

            if (moodSection.Length > 0 && otherSection.Length > 0)
                return moodSection + "\n\n" + otherSection;

            return moodSection.Length > 0 ? moodSection : otherSection;
        }

        readonly struct MoodTier
        {
            public MoodTier(MentalBreakIntensity? intensity, List<(BreakInfo info, float weight)>? entries, string? message)
            {
                Intensity = intensity;
                Entries = entries;
                Message = message;
            }

            public MentalBreakIntensity? Intensity { get; }
            public List<(BreakInfo info, float weight)>? Entries { get; }
            // Optional pre-rendered message that replaces the entries list (e.g. "blocked").
            public string? Message { get; }

            public static MoodTier Hidden => new(null, null, null);
            public static MoodTier WithMessage(string msg) => new(null, null, msg);
        }

        static MoodTier CollectMoodTier(Pawn pawn)
        {
            MentalBreaker? breaker = pawn.mindState?.mentalBreaker;
            if (breaker == null) return MoodTier.Hidden;
            if (breaker.Blocked) return MoodTier.WithMessage("BreakTimer.PossibleBreaksBlocked".Translate());
            if (!breaker.CanDoRandomMentalBreaks) return MoodTier.Hidden;

            MentalBreakIntensity? eligible = HighestEligibleIntensity(breaker);
            if (eligible is null) return MoodTier.Hidden;

            var entries = new List<(BreakInfo info, float weight)>(8);

            HashSet<MentalBreakDef>? ideoAllow = null;
            try { ideoAllow = pawn.Ideo?.cachedPossibleMentalBreaks; }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[BreakTimer] Ideo.cachedPossibleMentalBreaks threw: {ex.Message}",
                    Once.Id("ideo-breaks"));
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
                        Once.Id("possible-list", info.DefName));
                }
            }

            return new MoodTier(eligible, entries, null);
        }

        static string RenderMoodTier(MoodTier tier, IReadOnlyList<string> labels, int labelOffset)
        {
            if (tier.Message != null) return tier.Message;
            if (tier.Entries == null || tier.Intensity is null) return string.Empty;

            if (tier.Entries.Count == 0)
                return "BreakTimer.PossibleBreaksNoneEligible".Translate(tier.Intensity.Value.ToString());

            float total = 0f;
            foreach (var e in tier.Entries) total += e.weight;

            var ordered = tier.Entries
                .Select((entry, idx) => (entry.info, entry.weight, idx))
                .OrderByDescending(t => t.weight);

            var sb = new StringBuilder();
            sb.AppendLine("BreakTimer.PossibleBreaksHeader".Translate(tier.Intensity.Value.ToString()));
            foreach (var (info, weight, idx) in ordered)
            {
                float pct = total > 0f ? weight / total : 0f;
                string label = labels[labelOffset + idx];
                if (info.Requirements.AnomalousBreak) label += " " + "BreakTimer.AnomalySuffix".Translate();
                sb.Append(" ").Append(label).Append(" - ").AppendLine(pct.ToStringPercent("0"));
            }

            return sb.ToString().TrimEnd();
        }

        readonly struct ExtraTrigger
        {
            public ExtraTrigger(string label, string defName, string source, float mtbDays)
            {
                Label = label;
                DefName = defName;
                Source = source;
                MtbDays = mtbDays;
            }

            public string Label { get; }
            public string DefName { get; }
            public string Source { get; }
            public float MtbDays { get; }
        }

        static string RenderOtherTriggers(List<ExtraTrigger> extras, IReadOnlyList<string> labels, int labelOffset)
        {
            if (extras == null || extras.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("BreakTimer.OtherStatesHeader".Translate());
            for (int i = 0; i < extras.Count; i++)
            {
                ExtraTrigger e = extras[i];
                sb.Append(" ").AppendLine(
                    "BreakTimer.OtherStatesEntry".Translate(labels[labelOffset + i], e.Source, FormatMtb(e.MtbDays)));
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
                                state.defName,
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
                                state.defName,
                                sourceTag,
                                mtb));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.WarningOnce(
                        $"[BreakTimer] Trait giver scan failed for {trait?.def?.defName ?? "?"}: {ex.Message}",
                        Once.Id("trait-giver", trait?.def?.defName));
                }
            }
        }

        static void CollectHediffTriggers(Pawn pawn, List<ExtraTrigger> sink)
        {
            HediffSet? hediffSet = pawn.health?.hediffSet;
            List<Hediff>? hediffs = hediffSet?.hediffs;
            if (hediffs == null || hediffs.Count == 0) return;

            foreach (Hediff hediff in hediffs)
            {
                if (hediff == null) continue;
                try
                {
                    HediffStage? stage = hediff.CurStage;
                    List<MentalStateGiver>? givers = stage?.mentalStateGivers;
                    if (givers == null || givers.Count == 0) continue;

                    string hediffLabel = hediff.LabelCap;
                    if (string.IsNullOrEmpty(hediffLabel))
                        hediffLabel = hediff.def?.LabelCap.RawText ?? hediff.def?.defName ?? "BreakTimer.FallbackHediff".Translate().ToString();
                    string sourceTag = "BreakTimer.SourceCondition".Translate(hediffLabel);

                    foreach (MentalStateGiver giver in givers)
                    {
                        if (giver?.mentalState == null) continue;
                        if (giver.mtbDays <= 0f || float.IsInfinity(giver.mtbDays) || float.IsNaN(giver.mtbDays))
                            continue;

                        MentalStateDef state = giver.mentalState;
                        if (state.Worker != null && !state.Worker.StateCanOccur(pawn)) continue;

                        sink.Add(new ExtraTrigger(
                            BreakLabels.ForState(state),
                            state.defName,
                            sourceTag,
                            giver.mtbDays));
                    }
                }
                catch (Exception ex)
                {
                    Log.WarningOnce(
                        $"[BreakTimer] Hediff giver scan failed for {hediff?.def?.defName ?? "?"}: {ex.Message}",
                        Once.Id("hediff-giver", hediff?.def?.defName));
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
                    string defName = state != null ? state.defName : fit.defName;
                    sink.Add(new ExtraTrigger(label, defName, stage, mtb));
                }
                catch (Exception ex)
                {
                    Log.WarningOnce(
                        $"[BreakTimer] MentalFit scan failed for {fit?.defName ?? "?"}: {ex.Message}",
                        Once.Id("mental-fit", fit?.defName));
                }
            }
        }

        static string FormatMtb(float mtbDays)
        {
            if (mtbDays >= 1f) return Mathf.RoundToInt(mtbDays).ToString() + "d";
            int hours = Mathf.Max(1, Mathf.CeilToInt(mtbDays * 24f));
            return hours.ToString() + "h";
        }

        // Highest intensity the pawn qualifies for off CurMood vs the per-tier thresholds.
        // Mirrors MentalBreaker.CurrentDesiredMoodBreakIntensity, minus the 2000-tick dwell
        // requirement (too coarse for a snapshot tooltip).
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

        static void AppendDateLine(StringBuilder sb, string label, long absTick, Vector2 longLat)
        {
            sb.Append(label).AppendLine(":");
            sb.Append("  ").AppendLine(GenDate.DateFullStringWithHourAt(absTick, longLat));
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
