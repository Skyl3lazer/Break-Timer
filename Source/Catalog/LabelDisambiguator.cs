using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BreakTimer
{
    // Turns items that share a friendly label into unique display labels by appending a
    // parenthesised fragment of each item's defName, split at word boundaries (PascalCase
    // plus _ / - separators).
    public static class LabelDisambiguator
    {
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

                // Same defName from two sources: nothing to disambiguate with, reuse the base.
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

        // Given a shared base label and the defNames colliding on it, returns one label per
        // defName in input order.
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
                        // Two defs that overlap word-for-word; fall back to the full defName.
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

        // Splits a defName into words at PascalCase boundaries (lowercase-or-digit → upper)
        // and _ / - / space separators. Acronyms stay glued together ("VFEC" → one word).
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
