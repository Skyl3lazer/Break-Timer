using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BreakTimer
{
    // Cached, derived metadata for a single MentalBreakDef: identity, intensity, commonality,
    // requirements, recovery profile. Built once at startup and queried freely thereafter.
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

        // Raw lowercase label (use LabelCap for display). Collision disambiguation is applied
        // contextually at render time, against only the items visible in a given tooltip.
        public string Label { get; }

        public string LabelCap { get; }
        public MentalBreakWorker Worker => Def.Worker;

        // Authoritative "can this break happen to this pawn now?" — delegates to the worker,
        // which covers both BreakRequirements and worker-specific gating (food on hand, exit
        // reachable, ...).
        public bool CanOccurFor(Pawn pawn)
        {
            if (pawn is null) return false;
            try { return Worker.BreakCanOccur(pawn); }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[BreakTimer] BreakCanOccur threw for {DefName} on {pawn.LabelShort}: {ex.Message}",
                    Once.Id("CanOccurFor", DefName));
                return false;
            }
        }

        // Mirror of MentalBreakWorker.CommonalityFor for ranking.
        public float CommonalityFor(Pawn pawn, bool moodCaused = false)
        {
            if (pawn is null) return 0f;
            try { return Mathf.Max(0f, Worker.CommonalityFor(pawn, moodCaused)); }
            catch (Exception ex)
            {
                Log.WarningOnce(
                    $"[BreakTimer] CommonalityFor threw for {DefName} on {pawn.LabelShort}: {ex.Message}",
                    Once.Id("CommonalityFor", DefName));
                return 0f;
            }
        }

        // Human-readable reasons this break can't happen to the pawn — declarative
        // requirements plus the worker check. Kept for future "why not?" UI.
        public IEnumerable<string> GetUnmetReasons(Pawn pawn)
        {
            if (pawn is null) yield break;

            foreach (string r in Requirements.GetUnmetReasons(pawn))
                yield return r;

            if (Requirements.DeclarativelyAllowsPawn(pawn) && !CanOccurFor(pawn))
                yield return "BreakTimer.ReqWorkerPrereqs".Translate();
        }

        public override string ToString()
            => $"BreakInfo({DefName}, {Intensity}, {(MentalState?.defName ?? "no state")})";
    }
}
