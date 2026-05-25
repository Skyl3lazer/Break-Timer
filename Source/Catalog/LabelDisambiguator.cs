using System.Collections.Generic;
using System.Text;

namespace BreakTimer
{
	/// <summary>
	/// Stateless helper that, given a colliding base label and the defNames of the defs
	/// that share it, produces unique human-readable variants by extracting the part of
	/// each defName that differs (after stripping common prefix and suffix) and pasting
	/// it back as a parenthesised tag — e.g. <c>Disambiguate("insulting spree",
	/// ["InsultingSpree", "TargetedInsultingSpree"])</c> yields
	/// <c>["insulting spree", "insulting spree (Targeted)"]</c>.
	/// </summary>
	public static class LabelDisambiguator
	{
		public static List<string> Disambiguate(string baseLabel, IList<string> defNames)
		{
			var result = new List<string>(defNames.Count);
			if (defNames == null || defNames.Count <= 1)
			{
				if (defNames != null)
					foreach (string _ in defNames) result.Add(baseLabel);
				return result;
			}

			string prefix = LongestCommonPrefix(defNames);
			string suffix = LongestCommonSuffix(defNames);

			int shortest = int.MaxValue;
			foreach (string n in defNames) if (n.Length < shortest) shortest = n.Length;
			if (prefix.Length + suffix.Length >= shortest)
				suffix = string.Empty;

			bool baseSlotUsed = false;
			for (int i = 0; i < defNames.Count; i++)
			{
				string original = defNames[i] ?? string.Empty;
				string unique = original;
				if (prefix.Length > 0 && unique.StartsWith(prefix))
					unique = unique.Substring(prefix.Length);
				if (suffix.Length > 0 && unique.EndsWith(suffix))
					unique = unique.Substring(0, unique.Length - suffix.Length);

				if (unique.Length == 0)
				{
					if (!baseSlotUsed)
					{
						result.Add(baseLabel);
						baseSlotUsed = true;
					}
					else
					{
						result.Add(baseLabel + " (" + Prettify(original) + ")");
					}
				}
				else
				{
					result.Add(baseLabel + " (" + Prettify(unique) + ")");
				}
			}
			return result;
		}

		static string LongestCommonPrefix(IList<string> strings)
		{
			if (strings.Count == 0) return string.Empty;
			string first = strings[0] ?? string.Empty;
			int maxLen = first.Length;
			for (int i = 1; i < strings.Count; i++)
			{
				string s = strings[i] ?? string.Empty;
				if (s.Length < maxLen) maxLen = s.Length;
			}
			int matched = 0;
			while (matched < maxLen)
			{
				char c = first[matched];
				for (int i = 1; i < strings.Count; i++)
				{
					if (strings[i][matched] != c) return first.Substring(0, matched);
				}
				matched++;
			}
			return first.Substring(0, matched);
		}

		static string LongestCommonSuffix(IList<string> strings)
		{
			if (strings.Count == 0) return string.Empty;
			string first = strings[0] ?? string.Empty;
			int maxLen = first.Length;
			for (int i = 1; i < strings.Count; i++)
			{
				string s = strings[i] ?? string.Empty;
				if (s.Length < maxLen) maxLen = s.Length;
			}
			int matched = 0;
			while (matched < maxLen)
			{
				char c = first[first.Length - 1 - matched];
				for (int i = 1; i < strings.Count; i++)
				{
					string s = strings[i];
					if (s[s.Length - 1 - matched] != c) return first.Substring(first.Length - matched);
				}
				matched++;
			}
			return first.Substring(first.Length - matched);
		}

		/// <summary>
		/// Turns a CamelCase / snake_case defName fragment into a friendlier
		/// space-separated label, e.g. <c>"TargetedInsulting"</c> →
		/// <c>"Targeted Insulting"</c>, <c>"DrugMajor"</c> → <c>"Drug Major"</c>,
		/// <c>"Wander_Sad"</c> → <c>"Wander Sad"</c>.
		/// </summary>
		static string Prettify(string raw)
		{
			if (string.IsNullOrEmpty(raw)) return raw;
			var sb = new StringBuilder(raw.Length + 8);
			for (int i = 0; i < raw.Length; i++)
			{
				char c = raw[i];
				if (c == '_' || c == '-')
				{
					if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
					continue;
				}
				if (i > 0 && char.IsUpper(c))
				{
					char prev = raw[i - 1];
					if (!char.IsUpper(prev) && prev != '_' && prev != '-' && prev != ' ')
						sb.Append(' ');
				}
				sb.Append(c);
			}
			return sb.ToString().Trim();
		}
	}
}
