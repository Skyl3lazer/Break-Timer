using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BreakTimer
{
	/// <summary>
	/// Stateless helper that turns a list of items sharing the same friendly label into
	/// a parallel list of unique display labels by appending a parenthesised fragment
	/// derived from each item's defName. Stripping happens at <em>word boundaries</em>
	/// (PascalCase + <c>_</c> / <c>-</c> separators), never mid-word, so
	/// <c>"Wander_Sad"</c> reduces to <c>"Sad"</c> and never to <c>"ander Sad"</c>.
	/// </summary>
	/// <remarks>
	/// Designed to be called per-tooltip-render with the set of items currently in view:
	/// disambiguation only kicks in for raw-label groups with more than one visible
	/// member, so an unambiguous status keeps its plain label even if a global label
	/// collision exists elsewhere in the def database.
	/// </remarks>
	public static class LabelDisambiguator
	{
		/// <summary>
		/// Returns a list of final display labels parallel to <paramref name="items"/>.
		/// Items whose raw label is unique within <paramref name="items"/> get that label
		/// back unchanged. Items sharing a raw label get a parenthesised disambiguator
		/// computed from their defName's unique word fragment.
		/// </summary>
		public static List<string> Resolve(IList<(string label, string defName)> items)
		{
			if (items == null || items.Count == 0) return new List<string>();

			var resolved = new string[items.Count];
			var indexed = new List<(string label, string defName, int idx)>(items.Count);
			for (int i = 0; i < items.Count; i++)
				indexed.Add((items[i].label ?? string.Empty, items[i].defName ?? string.Empty, i));

			foreach (var group in indexed.GroupBy(t => t.label, StringComparer.OrdinalIgnoreCase))
			{
				var members = group.ToList();
				if (members.Count == 1)
				{
					resolved[members[0].idx] = group.Key;
					continue;
				}

				// All members share the same defName (e.g. the same trait giver listed
				// twice from two sources). Nothing useful to disambiguate with — just
				// reuse the base label everywhere.
				if (members.Select(m => m.defName).Distinct(StringComparer.Ordinal).Count() == 1)
				{
					foreach (var m in members) resolved[m.idx] = group.Key;
					continue;
				}

				var defNames = members.Select(m => m.defName).ToList();
				List<string> labels = Disambiguate(group.Key, defNames);
				for (int i = 0; i < members.Count; i++)
					resolved[members[i].idx] = labels[i];
			}

			return resolved.ToList();
		}

		/// <summary>
		/// Lower-level entry point: given a shared base label and the defNames of the
		/// defs colliding on it, returns one label per defName in input order.
		/// </summary>
		public static List<string> Disambiguate(string baseLabel, IList<string> defNames)
		{
			var result = new List<string>(defNames?.Count ?? 0);
			if (defNames == null || defNames.Count == 0) return result;
			if (defNames.Count == 1)
			{
				result.Add(baseLabel);
				return result;
			}

			var wordLists = new List<List<string>>(defNames.Count);
			foreach (string n in defNames) wordLists.Add(SplitWords(n ?? string.Empty));

			int leading = CountCommonLeadingWords(wordLists);
			int trailing = CountCommonTrailingWords(wordLists, leading);

			bool baseSlotUsed = false;
			for (int i = 0; i < defNames.Count; i++)
			{
				List<string> words = wordLists[i];
				int uniqueCount = words.Count - leading - trailing;

				if (uniqueCount <= 0)
				{
					if (!baseSlotUsed)
					{
						result.Add(baseLabel);
						baseSlotUsed = true;
					}
					else
					{
						// Two defs that completely overlap word-wise; fall back to the
						// raw defName so the user can still tell them apart.
						result.Add(baseLabel + " (" + JoinWords(words) + ")");
					}
				}
				else
				{
					List<string> unique = words.GetRange(leading, uniqueCount);
					result.Add(baseLabel + " (" + JoinWords(unique) + ")");
				}
			}
			return result;
		}

		/// <summary>
		/// Splits a defName fragment into its constituent words. PascalCase boundaries
		/// (lowercase-or-digit → uppercase) and <c>_</c> / <c>-</c> / space separators
		/// all start a new word. Acronyms stay glued together (<c>"VFEC"</c> → one word).
		/// </summary>
		static List<string> SplitWords(string s)
		{
			var words = new List<string>();
			if (string.IsNullOrEmpty(s)) return words;

			int start = 0;
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c == '_' || c == '-' || c == ' ')
				{
					if (i > start) words.Add(s.Substring(start, i - start));
					start = i + 1;
					continue;
				}

				if (i > start && char.IsUpper(c))
				{
					char prev = s[i - 1];
					if (char.IsLetterOrDigit(prev) && !char.IsUpper(prev))
					{
						words.Add(s.Substring(start, i - start));
						start = i;
					}
				}
			}
			if (start < s.Length) words.Add(s.Substring(start));
			return words;
		}

		static int CountCommonLeadingWords(List<List<string>> wordLists)
		{
			if (wordLists.Count == 0) return 0;
			int min = int.MaxValue;
			foreach (List<string> l in wordLists) if (l.Count < min) min = l.Count;

			int matched = 0;
			while (matched < min)
			{
				string word = wordLists[0][matched];
				for (int j = 1; j < wordLists.Count; j++)
				{
					if (!string.Equals(wordLists[j][matched], word, StringComparison.OrdinalIgnoreCase))
						return matched;
				}
				matched++;
			}
			return matched;
		}

		static int CountCommonTrailingWords(List<List<string>> wordLists, int leadingCommon)
		{
			if (wordLists.Count == 0) return 0;
			int min = int.MaxValue;
			foreach (List<string> l in wordLists)
			{
				int avail = l.Count - leadingCommon;
				if (avail < min) min = avail;
			}
			if (min <= 0) return 0;

			int matched = 0;
			while (matched < min)
			{
				List<string> first = wordLists[0];
				string word = first[first.Count - 1 - matched];
				for (int j = 1; j < wordLists.Count; j++)
				{
					List<string> lst = wordLists[j];
					string w = lst[lst.Count - 1 - matched];
					if (!string.Equals(w, word, StringComparison.OrdinalIgnoreCase))
						return matched;
				}
				matched++;
			}
			return matched;
		}

		static string JoinWords(IList<string> words)
		{
			if (words == null || words.Count == 0) return string.Empty;
			if (words.Count == 1) return words[0];
			var sb = new StringBuilder(words.Sum(w => w.Length) + words.Count);
			for (int i = 0; i < words.Count; i++)
			{
				if (i > 0) sb.Append(' ');
				sb.Append(words[i]);
			}
			return sb.ToString();
		}
	}
}
