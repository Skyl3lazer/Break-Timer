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

        // Delegates to the worker, which covers worker-specific gating (food on hand, exit
        // reachable) beyond BreakRequirements.
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

        public IEnumerable<string> GetUnmetReasons(Pawn pawn)
        {
            if (pawn is null) yield break;

            foreach (string r in Requirements.GetUnmetReasons(pawn))
                yield return r;

            if (Requirements.DeclarativelyAllowsPawn(pawn) && !CanOccurFor(pawn))
                yield return TraitRestrictionReason(pawn) ?? "BreakTimer.ReqNotPossibleNow".Translate();
        }

        // Trait gates BreakCanOccur enforces that aren't declarative break requirements, so they
        // read as a named trait rather than the opaque catch-all.
        string? TraitRestrictionReason(Pawn pawn)
        {
            TraitSet? traits = pawn.story?.traits;
            if (traits == null) return null;

            if (MentalState != null)
            {
                foreach (Trait t in traits.allTraits)
                {
                    if (t.Suppressed) continue;
                    List<MentalStateDef>? disallowed = t.CurrentData?.disallowedMentalStates;
                    if (disallowed != null && disallowed.Contains(MentalState))
                        return "BreakTimer.ReqTraitDisallows".Translate(BreakLabels.ForTrait(t));
                }
            }

            Trait? restrictor = OnlyAllowedRestrictor(pawn, traits);
            if (restrictor != null)
                return "BreakTimer.ReqTraitOnlyAllows".Translate(BreakLabels.ForTrait(restrictor));

            return null;
        }

        // Mirrors BreakCanOccur's only-allowed clause, which blocks this break only when an allowed
        // same-intensity break can actually occur - otherwise the real block is elsewhere.
        Trait? OnlyAllowedRestrictor(Pawn pawn, TraitSet traits)
        {
            var allowed = new List<MentalBreakDef>();
            foreach (MentalBreakDef d in traits.TheOnlyAllowedMentalBreaks) allowed.Add(d);
            if (allowed.Count == 0 || allowed.Contains(Def)) return null;

            bool anAllowedCanOccur = false;
            foreach (MentalBreakDef d in allowed)
            {
                if (d.intensity != Intensity) continue;
                try { if (d.Worker.BreakCanOccur(pawn)) { anAllowedCanOccur = true; break; } }
                catch { /* a worker throwing is treated as cannot-occur */ }
            }
            if (!anAllowedCanOccur) return null;

            foreach (Trait t in traits.allTraits)
            {
                if (t.Suppressed) continue;
                List<MentalBreakDef>? only = t.CurrentData?.theOnlyAllowedMentalBreaks;
                if (only != null && only.Count > 0 && !only.Contains(Def))
                    return t;
            }
            return null;
        }

        public override string ToString()
            => $"BreakInfo({DefName}, {Intensity}, {(MentalState?.defName ?? "no state")})";
    }
}
