using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BreakTimer
{
	/// <summary>
	/// Process-wide label cache for everything the tooltip needs to render. Backs the
	/// "Possible mental breaks" section, the active-break header, and the friendly-name
	/// lookups that don't live on <see cref="BreakInfo"/>. Every value cached here is keyed
	/// on a <see cref="Def"/> (plus, for traits, the immutable degree value), so entries
	/// are valid for the whole session — defs never get re-loaded after game startup.
	/// </summary>
	/// <remarks>
	/// <para>
	/// State and break labels are eagerly pre-filled on first access via
	/// <see cref="DefDatabase{T}.AllDefsListForReading"/>. The pre-fill step runs the
	/// <see cref="LabelDisambiguator"/> over every group of defs that resolve to the same
	/// friendly label, so e.g. the two vanilla <c>MentalBreakDef</c>s that both inherit
	/// the label <c>"insulting spree"</c> end up as <c>"Insulting spree"</c> and
	/// <c>"Insulting spree (Targeted)"</c>.
	/// </para>
	/// <para>
	/// Read-only access from the UI thread, so a plain <see cref="Dictionary{TKey,TValue}"/>
	/// is sufficient. Tooltip rendering does up to a couple of dozen lookups per hover
	/// frame; pre-caching these saves the per-call <c>LabelCap.RawText</c> resolution and
	/// the per-call <see cref="GenText.CapitalizeFirst(string)"/> allocation.
	/// </para>
	/// </remarks>
	public static class BreakLabels
	{
		static readonly object syncRoot = new();

		static bool stateMapReady;
		static readonly Dictionary<MentalStateDef, string> stateLabel = new();
		static readonly Dictionary<MentalStateDef, string> stateLabelCap = new();

		static bool breakMapReady;
		static readonly Dictionary<MentalBreakDef, string> breakLabel = new();
		static readonly Dictionary<MentalBreakDef, string> breakLabelCap = new();

		static readonly Dictionary<MentalFitDef, string> fitLabelCap = new();
		static readonly Dictionary<TraitKey, string> traitLabelCap = new();
		static readonly Dictionary<TraitKey, string> traitSourceText = new();

		public static string ForBreak(MentalBreakDef? def)
		{
			if (def is null) return string.Empty;
			EnsureBreakMap();
			return breakLabel.TryGetValue(def, out string cached) ? cached : def.defName;
		}

		public static string ForBreakCap(MentalBreakDef? def)
		{
			if (def is null) return string.Empty;
			EnsureBreakMap();
			return breakLabelCap.TryGetValue(def, out string cached) ? cached : def.defName;
		}

		public static string ForState(MentalStateDef? state)
		{
			if (state is null) return string.Empty;
			EnsureStateMap();
			return stateLabelCap.TryGetValue(state, out string cached)
				? cached
				: (state.LabelCap.RawText ?? state.defName);
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

		static void EnsureStateMap()
		{
			if (stateMapReady) return;
			lock (syncRoot)
			{
				if (stateMapReady) return;
				BuildMap(
					DefDatabase<MentalStateDef>.AllDefsListForReading,
					stateLabel,
					stateLabelCap,
					ResolveStateRawLabel);
				stateMapReady = true;
			}
		}

		static void EnsureBreakMap()
		{
			if (breakMapReady) return;
			lock (syncRoot)
			{
				if (breakMapReady) return;
				BuildMap(
					DefDatabase<MentalBreakDef>.AllDefsListForReading,
					breakLabel,
					breakLabelCap,
					ResolveBreakRawLabel);
				breakMapReady = true;
			}
		}

		static void BuildMap<T>(
			IList<T> defs,
			Dictionary<T, string> labelOut,
			Dictionary<T, string> labelCapOut,
			Func<T, string> rawLabelOf) where T : Def
		{
			var raw = new Dictionary<T, string>(defs.Count);
			foreach (T def in defs)
			{
				if (def is null) continue;
				raw[def] = rawLabelOf(def);
			}

			foreach (var group in raw.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase))
			{
				var members = group.ToList();
				if (members.Count == 1)
				{
					labelOut[members[0].Key] = group.Key;
					continue;
				}

				var keys = members.Select(kv => kv.Key).ToList();
				var defNames = keys.Select(d => d.defName).ToList();
				var disambiguated = LabelDisambiguator.Disambiguate(group.Key, defNames);
				for (int i = 0; i < keys.Count; i++)
					labelOut[keys[i]] = disambiguated[i];
			}

			foreach (var kv in labelOut)
				labelCapOut[kv.Key] = kv.Value.CapitalizeFirst();
		}

		static string ResolveBreakRawLabel(MentalBreakDef def)
		{
			if (!def.label.NullOrEmpty()) return def.label;
			if (def.mentalState != null && !def.mentalState.label.NullOrEmpty())
				return def.mentalState.label;
			return def.defName;
		}

		static string ResolveStateRawLabel(MentalStateDef def)
		{
			return def.label.NullOrEmpty() ? def.defName : def.label;
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
