using System.Collections.Generic;
using RimWorld;
using Verse;

namespace BreakTimer
{
    // Process-wide cache of raw friendly labels (before any disambiguation). Tooltip code
    // reads these, then asks LabelDisambiguator to add suffixes only where the visible set
    // actually has duplicates.
    public static class BreakLabels
    {
        static readonly Dictionary<MentalStateDef, string> stateLabelCap = new();
        static readonly Dictionary<MentalBreakDef, string> breakLabel = new();
        static readonly Dictionary<MentalBreakDef, string> breakLabelCap = new();
        static readonly Dictionary<MentalFitDef, string> fitLabelCap = new();
        static readonly Dictionary<TraitKey, string> traitLabelCap = new();
        static readonly Dictionary<TraitKey, string> traitSourceText = new();

        public static string ForBreak(MentalBreakDef? def)
        {
            if (def is null) return string.Empty;
            if (breakLabel.TryGetValue(def, out string cached)) return cached;

            string raw = ResolveBreakRawLabel(def);
            breakLabel[def] = raw;
            return raw;
        }

        public static string ForBreakCap(MentalBreakDef? def)
        {
            if (def is null) return string.Empty;
            if (breakLabelCap.TryGetValue(def, out string cached)) return cached;

            string capped = ForBreak(def).CapitalizeFirst();
            breakLabelCap[def] = capped;
            return capped;
        }

        public static string ForState(MentalStateDef? state)
        {
            if (state is null) return string.Empty;
            if (stateLabelCap.TryGetValue(state, out string cached)) return cached;

            string raw = state.LabelCap.RawText;
            string resolved =
                !raw.NullOrEmpty() ? raw
                : !state.label.NullOrEmpty() ? state.label.CapitalizeFirst()
                : state.defName;

            stateLabelCap[state] = resolved;
            return resolved;
        }

        public static string ForFit(MentalFitDef? fit)
        {
            if (fit is null) return string.Empty;
            if (fitLabelCap.TryGetValue(fit, out string cached)) return cached;

            string resolved = !fit.label.NullOrEmpty()
                ? fit.label.CapitalizeFirst()
                : fit.defName;

            fitLabelCap[fit] = resolved;
            return resolved;
        }

        public static string ForTrait(Trait trait)
        {
            if (trait?.def is null) return string.Empty;
            TraitKey key = new(trait.def, trait.Degree);
            if (traitLabelCap.TryGetValue(key, out string cached)) return cached;

            string label = trait.LabelCap;
            if (label.NullOrEmpty()) label = trait.def.defName;

            traitLabelCap[key] = label;
            return label;
        }

        // Pre-formatted "trait: <label>" source tag for the "Other potential states"
        // tooltip section, keyed by trait def + degree.
        public static string TraitSourceTag(Trait trait)
        {
            if (trait?.def is null) return "BreakTimer.SourceTraitFallback".Translate();
            TraitKey key = new(trait.def, trait.Degree);
            if (traitSourceText.TryGetValue(key, out string cached)) return cached;

            string built = "BreakTimer.SourceTrait".Translate(ForTrait(trait));
            traitSourceText[key] = built;
            return built;
        }

        static string ResolveBreakRawLabel(MentalBreakDef def)
        {
            if (!def.label.NullOrEmpty()) return def.label;
            if (def.mentalState != null && !def.mentalState.label.NullOrEmpty())
                return def.mentalState.label;
            return def.defName;
        }

        readonly struct TraitKey : System.IEquatable<TraitKey>
        {
            public TraitKey(TraitDef def, int degree)
            {
                Def = def;
                Degree = degree;
            }

            public TraitDef Def { get; }
            public int Degree { get; }

            public bool Equals(TraitKey other) => ReferenceEquals(Def, other.Def) && Degree == other.Degree;
            public override bool Equals(object obj) => obj is TraitKey k && Equals(k);
            public override int GetHashCode()
                => unchecked(((Def?.GetHashCode() ?? 0) * 397) ^ Degree);
        }
    }
}
