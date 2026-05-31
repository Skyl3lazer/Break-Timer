using System.Collections.Generic;
using RimWorld;
using Verse;

namespace BreakTimer
{
	/// <summary>
	/// Process-wide cache of <em>raw</em> friendly labels — the value before any
	/// contextual disambiguation pass runs. Tooltip code reads these as the starting
	/// point and then asks <see cref="LabelDisambiguator"/> to add parenthesised
	/// suffixes only where the visible set actually contains duplicates.
	/// </summary>
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

		/// <summary>
		/// Pre-formatted "trait: &lt;label&gt;" string used as the source tag in the
		/// "Other potential states" tooltip section. Keyed by trait def + degree so it's
		/// stable for the session.
		/// </summary>
		public static string TraitSourceTag(Trait trait)
		{
			if (trait?.def is null) return "trait";
			TraitKey key = new(trait.def, trait.Degree);
			if (traitSourceText.TryGetValue(key, out string cached)) return cached;

			string built = "trait: " + ForTrait(trait);
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
