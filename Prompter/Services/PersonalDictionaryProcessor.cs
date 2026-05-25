using System.Text.RegularExpressions;
using Prompter.Models;

namespace Prompter.Services;

internal static class PersonalDictionaryProcessor
{
    public static string Process(string input, List<DictionaryEntry> entries, IFileLogger logger)
    {
        if (string.IsNullOrWhiteSpace(input) || entries.Count == 0)
            return input;

        var aliasMap = new List<(string alias, string canonical)>();
        var seenAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Word))
                continue;

            var aliases = entry.Aliases ?? new List<string>();
            if (aliases.Count == 0)
                continue;

            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                    continue;

                var trimmed = alias.Trim();
                if (seenAliases.TryGetValue(trimmed, out var existingCanonical))
                {
                    if (!existingCanonical.Equals(entry.Word, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.Log($"PersonalDictionary: duplicate alias '{trimmed}' for '{existingCanonical}' and '{entry.Word}'. First wins.");
                    }
                    continue;
                }

                seenAliases[trimmed] = entry.Word;
                aliasMap.Add((trimmed, entry.Word));
            }
        }

        if (aliasMap.Count == 0)
            return input;

        aliasMap = aliasMap.OrderByDescending(a => a.alias.Length).ToList();

        var escaped = string.Join("|", aliasMap.Select(a => Regex.Escape(a.alias)));
        var pattern = $@"(?<![\w'-])({escaped})(?![\w'-])";

        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = Regex.Replace(input, pattern, m =>
        {
            var matched = m.Groups[1].Value;
            if (seenAliases.TryGetValue(matched, out var canonical))
            {
                applied.Add(canonical);
                return canonical;
            }
            return matched;
        }, RegexOptions.IgnoreCase);

        if (applied.Count > 0)
        {
            logger.Log($"PersonalDictionary: locked {applied.Count} word(s) — {string.Join(", ", applied)}.");
        }

        return result;
    }
}
