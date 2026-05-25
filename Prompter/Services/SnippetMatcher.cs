using System.Text;
using Prompter.Models;

namespace Prompter.Services;

public class SnippetMatcher : ISnippetMatcher
{
    private static readonly char[] PunctuationToStrip = new[] { '.', ',', ';', ':', '!', '?', '\'', '"', '(', ')', '[', ']', '{', '}', '…', '-', '–', '—' };

    public Snippet? Match(string transcription, List<Snippet> snippets)
    {
        if (string.IsNullOrWhiteSpace(transcription) || snippets.Count == 0)
            return null;

        var normalizedInput = Normalize(transcription);
        if (string.IsNullOrWhiteSpace(normalizedInput))
            return null;

        Snippet? bestMatch = null;
        int bestDistance = int.MaxValue;
        int bestTriggerLength = -1;
        int bestIndex = int.MaxValue;

        for (int i = 0; i < snippets.Count; i++)
        {
            var snippet = snippets[i];
            if (string.IsNullOrWhiteSpace(snippet.Trigger))
                continue;

            var normalizedTrigger = Normalize(snippet.Trigger);
            if (string.IsNullOrWhiteSpace(normalizedTrigger))
                continue;

            // Length guard: prevent matching a short trigger inside a long sentence
            var lengthDelta = Math.Abs(normalizedInput.Length - normalizedTrigger.Length);
            if (lengthDelta > 4)
                continue;

            var distance = ComputeLevenshteinDistance(normalizedInput, normalizedTrigger);
            if (distance > 2)
                continue;

            bool isBetter = false;
            if (distance < bestDistance)
            {
                isBetter = true;
            }
            else if (distance == bestDistance)
            {
                if (normalizedTrigger.Length > bestTriggerLength)
                {
                    isBetter = true;
                }
                else if (normalizedTrigger.Length == bestTriggerLength && i < bestIndex)
                {
                    isBetter = true;
                }
            }

            if (isBetter)
            {
                bestMatch = snippet;
                bestDistance = distance;
                bestTriggerLength = normalizedTrigger.Length;
                bestIndex = i;
            }
        }

        return bestMatch;
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                // Collapse multiple spaces into one
                if (sb.Length == 0 || sb[^1] != ' ')
                    sb.Append(' ');
                continue;
            }

            if (Array.IndexOf(PunctuationToStrip, c) >= 0)
                continue;

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString().Trim();
    }

    private static int ComputeLevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // Optimize for typical short strings: use two rows instead of full matrix
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
