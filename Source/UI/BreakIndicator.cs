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

		// Tooltip text-cache. Without it, RimWorld's TipSignal re-invokes the getter on
		// every frame the hover is held (~60 Hz), so the entire collect → disambiguate →
		// render pipeline — including reflective MentalBreakWorker.BreakCanOccur and
		// MentalFitDef.CalculateMTBDays calls — runs against the live def database every
		// 16 ms. None of the inputs change that fast: mood ticks once per ~60 game ticks
		// and the displayed "Remaining" values round to whole hours. Caching the
		// rendered string for ~1 s real-time slashes work by ~60×; invalidating on
		// mental-state-def changes keeps "entering / leaving a break" instant.
		const float TooltipTextCacheTtlSeconds = 1.0f;

		static int cachedPawnId;
		static MentalStateDef? cachedActiveStateDef;
		static HediffDef? cachedActiveBreakHediffDef;
		static float cachedAtRealtime;
		static string? cachedTooltipText;

		static string GetCachedTooltip(Pawn? pawn)
		{
			if (pawn is null) return "(no pawn selected)";

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

		/// <summary>
		/// Invalidates the rendered-tooltip cache. Called from the mental-state
		/// start/end Harmony patches so a transition is reflected on the very next
		/// frame instead of waiting out the TTL window.
		/// </summary>
		public static void InvalidateTooltipCache()
		{
			cachedTooltipText = null;
		}

		static string BuildTooltip(Pawn? pawn)
		{
			if (pawn is null) return "(no pawn selected)";

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

			// Mood-driven breaks resolve through BreakInfo; everything else (hediff
			// givers like WanderConfused, trait givers, etc.) falls back to the broader
			// MentalStateInfo catalog so the player still gets a duration estimate.
			BreakDuration? duration =
				MentalBreakCatalog.GetForState(state.def)?.Duration
				?? MentalBreakCatalog.GetStateInfo(state.def)?.Duration;

			if (duration != null)
			{
				BreakDurationRemaining remaining = duration.GetRemaining(state);
				long minEnd = ToAbsTick(nowTick + remaining.MinTicks);
				long maxEnd = ToAbsTick(nowTick + remaining.MaxTicks);

				int minHours = TicksToHoursCeil(remaining.MinTicks);
				int maxHours = TicksToHoursCeil(remaining.MaxTicks);
				if (duration.HasUnboundedMax)
					sb.Append("Remaining: ").Append(minHours).AppendLine("h+");
				else if (minHours == maxHours)
					sb.Append("Remaining: ").Append(minHours).AppendLine("h");
				else
					sb.Append("Remaining: ").Append(minHours).Append("h - ").Append(maxHours).AppendLine("h");

				if (duration.HasUnboundedMax)
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

		// Catatonic is a MentalBreakDef with no <mentalState>: its worker applies the
		// CatatonicBreakdown hediff instead, so pawn.MentalState is always null for it.
		// Detection and timing live on CatatonicBreak so the patches stay in sync.
		static string BuildHediffBreakTooltip(Pawn pawn, Hediff hediff)
		{
			var sb = new StringBuilder();

			string label = CatatonicBreak.BreakDef != null
				? BreakLabels.ForBreakCap(CatatonicBreak.BreakDef)
				: hediff.LabelCap;
			sb.Append("Break: ").AppendLine(label);

			int nowTick = Find.TickManager.TicksGame;
			ActiveBreakRecord? rec = BreakTimerGameComponent.Instance?.GetActive(pawn);
			int startTick = rec?.startTick ?? (nowTick - Mathf.Max(0, hediff.ageTicks));
			Vector2 longLat = LongLatFor(pawn);
			sb.Append("Started: ").AppendLine(GenDate.DateFullStringWithHourAt(ToAbsTick(startTick), longLat));

			int remainingTicks = CatatonicBreak.RemainingTicks(hediff);
			if (remainingTicks > 0)
			{
				sb.Append("Remaining: ").Append(TicksToHoursCeil(remainingTicks)).AppendLine("h");
				sb.Append("Ends: ")
					.AppendLine(GenDate.DateFullStringWithHourAt(ToAbsTick(nowTick + remainingTicks), longLat));
			}

			string? description = hediff.def?.description;
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
		/// <remarks>
		/// Runs in three passes so disambiguation can be scoped to the visible content:
		/// (1) gather raw mood-tier and other-trigger entries, (2) ask
		/// <see cref="LabelDisambiguator"/> for unique display labels across the union of
		/// both sections, (3) render. This means a defName collision only produces a
		/// parenthesised tag when both colliding items are actually shown.
		/// </remarks>
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
				return "Possible mental breaks:\n No possible breaks!";

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
			/// <summary>Optional pre-rendered message that replaces the entries list (e.g. "blocked").</summary>
			public string? Message { get; }

			public static MoodTier Hidden => new(null, null, null);
			public static MoodTier WithMessage(string msg) => new(null, null, msg);
		}

		static MoodTier CollectMoodTier(Pawn pawn)
		{
			MentalBreaker? breaker = pawn.mindState?.mentalBreaker;
			if (breaker == null) return MoodTier.Hidden;
			if (breaker.Blocked) return MoodTier.WithMessage("Possible mental breaks:\n Breaks are currently blocked!");
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

			return new MoodTier(eligible, entries, null);
		}

		static string RenderMoodTier(MoodTier tier, IReadOnlyList<string> labels, int labelOffset)
		{
			if (tier.Message != null) return tier.Message;
			if (tier.Entries == null || tier.Intensity is null) return string.Empty;

			if (tier.Entries.Count == 0)
				return $"Possible mental breaks ({tier.Intensity.Value}):\n No currently eligible breaks!";

			float total = 0f;
			foreach (var e in tier.Entries) total += e.weight;

			var ordered = tier.Entries
				.Select((entry, idx) => (entry.info, entry.weight, idx))
				.OrderByDescending(t => t.weight);

			var sb = new StringBuilder();
			sb.Append("Possible mental breaks (").Append(tier.Intensity.Value).AppendLine("):");
			foreach (var (info, weight, idx) in ordered)
			{
				float pct = total > 0f ? weight / total : 0f;
				string label = labels[labelOffset + idx];
				if (info.Requirements.AnomalousBreak) label += " (anomaly)";
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
			sb.AppendLine("Other potential states:");
			for (int i = 0; i < extras.Count; i++)
			{
				ExtraTrigger e = extras[i];
				sb.Append(" ")
					.Append(labels[labelOffset + i])
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
						unchecked((int)0xB12D7200 ^ (trait?.def?.defName?.GetHashCode() ?? 0)));
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
						hediffLabel = hediff.def?.LabelCap.RawText ?? hediff.def?.defName ?? "hediff";
					string sourceTag = "Condition: " + hediffLabel;

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
						unchecked((int)0xB12D7400 ^ (hediff?.def?.defName?.GetHashCode() ?? 0)));
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
