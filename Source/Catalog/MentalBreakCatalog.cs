using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace BreakTimer
{
	/// <summary>
	/// Static reference catalog of every <see cref="MentalBreakDef"/> loaded by the game,
	/// resolved into rich <see cref="BreakInfo"/> records. Built once at startup (and
	/// rebuildable on demand) so consumers can do fast lookups by def, defName, intensity,
	/// category, or pawn eligibility.
	/// </summary>
	public static class MentalBreakCatalog
	{
		static readonly object buildLock = new();
		static bool built;

		static BreakInfo[] all = Array.Empty<BreakInfo>();
		static Dictionary<MentalBreakDef, BreakInfo> byDef = new();
		static Dictionary<string, BreakInfo> byDefName = new(StringComparer.Ordinal);
		static Dictionary<MentalBreakIntensity, BreakInfo[]> byIntensity = new();
		static Dictionary<MentalStateCategory, BreakInfo[]> byCategory = new();
		static Dictionary<MentalStateDef, BreakInfo> byState = new();
		static Dictionary<MentalStateDef, MentalStateInfo> stateInfoByDef = new();

		public static IReadOnlyList<BreakInfo> All
		{
			get { EnsureBuilt(); return all; }
		}

		public static IReadOnlyDictionary<MentalBreakDef, BreakInfo> ByDef
		{
			get { EnsureBuilt(); return byDef; }
		}

		public static IReadOnlyDictionary<string, BreakInfo> ByDefName
		{
			get { EnsureBuilt(); return byDefName; }
		}

		/// <summary>Lookup helper that returns null if the def is not registered.</summary>
		public static BreakInfo? Get(MentalBreakDef? def)
		{
			if (def is null) return null;
			EnsureBuilt();
			return byDef.TryGetValue(def, out BreakInfo info) ? info : null;
		}

		public static BreakInfo? Get(string? defName)
		{
			if (string.IsNullOrEmpty(defName)) return null;
			EnsureBuilt();
			return byDefName.TryGetValue(defName!, out BreakInfo info) ? info : null;
		}

		/// <summary>Returns the <see cref="BreakInfo"/> whose mental state matches, if any.</summary>
		public static BreakInfo? GetForState(MentalStateDef? state)
		{
			if (state is null) return null;
			EnsureBuilt();
			return byState.TryGetValue(state, out BreakInfo info) ? info : null;
		}

		/// <summary>
		/// Returns the <see cref="MentalStateInfo"/> for any defined <see cref="MentalStateDef"/>,
		/// regardless of whether it has a <see cref="MentalBreakDef"/>. This is the lookup
		/// for "the pawn is in mental state X — what's its duration and label?" cases that
		/// include hediff-driven states like <c>WanderConfused</c> (Dementia/Alzheimer's),
		/// trait givers, mental-fit givers, and scripted-incident states.
		/// </summary>
		public static MentalStateInfo? GetStateInfo(MentalStateDef? state)
		{
			if (state is null) return null;
			EnsureBuilt();
			return stateInfoByDef.TryGetValue(state, out MentalStateInfo info) ? info : null;
		}

		public static IReadOnlyList<BreakInfo> OfIntensity(MentalBreakIntensity intensity)
		{
			EnsureBuilt();
			return byIntensity.TryGetValue(intensity, out BreakInfo[] arr) ? arr : Array.Empty<BreakInfo>();
		}

		public static IReadOnlyList<BreakInfo> OfCategory(MentalStateCategory category)
		{
			EnsureBuilt();
			return byCategory.TryGetValue(category, out BreakInfo[] arr) ? arr : Array.Empty<BreakInfo>();
		}

		/// <summary>
		/// All breaks whose declarative requirements allow <paramref name="pawn"/>. This is
		/// the cheap preview filter: use <see cref="PossibleFor"/> for the authoritative
		/// answer (which also runs worker-specific checks).
		/// </summary>
		public static IEnumerable<BreakInfo> DeclarativelyAllowedFor(Pawn pawn)
		{
			if (pawn is null) yield break;
			EnsureBuilt();
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i].Requirements.DeclarativelyAllowsPawn(pawn))
					yield return all[i];
			}
		}

		/// <summary>
		/// Authoritative: every break whose worker reports it can happen to <paramref name="pawn"/>
		/// right now. Suitable for "what could this pawn possibly snap into?" UI.
		/// </summary>
		public static IEnumerable<BreakInfo> PossibleFor(Pawn pawn)
		{
			if (pawn is null) yield break;
			EnsureBuilt();
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i].CanOccurFor(pawn))
					yield return all[i];
			}
		}

		/// <summary>
		/// Returns the active mental break (if any) the pawn is currently in, by matching
		/// their live <see cref="MentalState"/> back to its owning <see cref="MentalBreakDef"/>.
		/// </summary>
		public static BreakInfo? GetActiveFor(Pawn pawn)
		{
			MentalState? active = pawn?.MentalState;
			return active != null ? GetForState(active.def) : null;
		}

		/// <summary>
		/// Builds the catalog if it hasn't been built yet. Safe to call multiple times.
		/// Called automatically from <see cref="BreakTimerMod"/> on game startup.
		/// </summary>
		public static void EnsureBuilt()
		{
			if (built) return;
			lock (buildLock)
			{
				if (built) return;
				Build();
				built = true;
			}
		}

		/// <summary>Force a rebuild. Mainly useful for dev reloads.</summary>
		public static void Rebuild()
		{
			lock (buildLock)
			{
				built = false;
				Build();
				built = true;
			}
		}

		static void Build()
		{
			List<MentalBreakDef> defs = DefDatabase<MentalBreakDef>.AllDefsListForReading;
			var infos = new List<BreakInfo>(defs.Count);

			byDef = new Dictionary<MentalBreakDef, BreakInfo>(defs.Count);
			byDefName = new Dictionary<string, BreakInfo>(defs.Count, StringComparer.Ordinal);
			byState = new Dictionary<MentalStateDef, BreakInfo>(defs.Count);

			for (int i = 0; i < defs.Count; i++)
			{
				MentalBreakDef def = defs[i];
				BreakInfo info;
				try
				{
					info = new BreakInfo(def);
				}
				catch (Exception ex)
				{
					Log.Error($"[BreakTimer] Failed to build BreakInfo for {def?.defName ?? "<null>"}: {ex}");
					continue;
				}

				infos.Add(info);
				byDef[def] = info;
				byDefName[def.defName] = info;
				if (info.MentalState != null && !byState.ContainsKey(info.MentalState))
					byState[info.MentalState] = info;
			}

			all = infos.ToArray();

			byIntensity = all
				.GroupBy(b => b.Intensity)
				.ToDictionary(g => g.Key, g => g.ToArray());

			byCategory = all
				.GroupBy(b => b.Category)
				.ToDictionary(g => g.Key, g => g.ToArray());

			List<MentalStateDef> stateDefs = DefDatabase<MentalStateDef>.AllDefsListForReading;
			stateInfoByDef = new Dictionary<MentalStateDef, MentalStateInfo>(stateDefs.Count);
			for (int i = 0; i < stateDefs.Count; i++)
			{
				MentalStateDef sdef = stateDefs[i];
				if (sdef == null) continue;
				try
				{
					stateInfoByDef[sdef] = new MentalStateInfo(sdef);
				}
				catch (Exception ex)
				{
					Log.Error($"[BreakTimer] Failed to build MentalStateInfo for {sdef.defName}: {ex}");
				}
			}

			if (Prefs.DevMode)
				Log.Message($"[BreakTimer] Cached {all.Length} mental break defs across {byIntensity.Count} intensity buckets, and {stateInfoByDef.Count} mental state defs.");
		}
	}
}
