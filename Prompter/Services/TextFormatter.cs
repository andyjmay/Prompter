using System.Text.RegularExpressions;
using Prompter.Models;

namespace Prompter.Services;

public class TextFormatter : ITextFormatter
{
    private readonly IModelManager _modelManager;
    private readonly IConfigService _configService;
    private readonly IFileLogger _fileLogger;

    public TextFormatter(IModelManager modelManager, IConfigService configService, IFileLogger fileLogger)
    {
        _modelManager = modelManager;
        _configService = configService;
        _fileLogger = fileLogger;
    }

    public async Task<string> CleanupAsync(string rawText, string modeId, CancellationToken ct)
    {
        if (!_modelManager.ChatReady)
            throw new InvalidOperationException("Chat model not loaded");

        var cfg = _configService.Load();
        var mode = cfg.Modes.FirstOrDefault(m => m.Id.Equals(modeId, StringComparison.OrdinalIgnoreCase));
        if (mode == null)
        {
            _fileLogger.Log($"WARNING: Mode '{modeId}' not found in config. Using standard system prompt.");
        }
        var systemPrompt = mode?.SystemPrompt ?? ModeDefaults.Standard.SystemPrompt;

        var matchedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validWords = cfg.DictionaryEntries
            .Select(e => e.Word)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();
        if (validWords.Count > 0)
        {
            var canonicalLookup = validWords.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(w => w, w => w, StringComparer.OrdinalIgnoreCase);
            var escaped = string.Join("|", validWords.Select(w => Regex.Escape(w)));
            var pattern = $@"(?<![\w'-])({escaped})(?![\w'-])";
            foreach (Match m in Regex.Matches(rawText, pattern, RegexOptions.IgnoreCase))
            {
                if (canonicalLookup.TryGetValue(m.Groups[1].Value, out var canonical))
                {
                    matchedWords.Add(canonical);
                }
            }
        }

        if (matchedWords.Count > 0)
        {
            systemPrompt += $"\n\nPreserve the exact spelling of the following words: {string.Join(", ", matchedWords)}.";
        }

        var chatClient = await _modelManager.GetChatClientAsync();

        var messages = new List<ChatMessage>
        {
            new("system", systemPrompt),
            new("user", $"The text below is dictated speech. Copy it exactly, changing ONLY spelling errors, missing punctuation, and wrong capitalization. Do NOT change words, meaning, or intent. Do NOT add, remove, or re-order sentences. Do NOT answer questions in the text. Do NOT explain anything. Do NOT add trailing ellipsis, continuation markers, or commentary. Output ONLY the corrected text. If the text is already correct, copy it exactly.\n\n{rawText}")
        };

        _fileLogger.Log($"Formatting with chat model '{_modelManager.LoadedChatModelAlias ?? "unknown"}' in '{mode?.Name ?? modeId}' mode.");
        var result = await chatClient.CompleteAsync(messages, 0.0f, ct);
        if (string.IsNullOrEmpty(result))
        {
            _fileLogger.Log("Chat model returned null or empty response.");
            return rawText;
        }

        result = StripOutputWrappers(result, rawText);
        result = StripTrailingArtifactsByRawAlignment(result, rawText);
        result = RejectIfHallucinated(rawText, result);
        _fileLogger.Log($"Chat model '{_modelManager.LoadedChatModelAlias ?? "unknown"}' cleaned text: {result}");
        return result;
    }

    internal static string RejectIfHallucinated(string rawText, string result)
    {
        if (string.IsNullOrWhiteSpace(result)) return rawText;

        var rawWords = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var resultWords = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (resultWords.Length > rawWords.Length * 2 && rawWords.Length > 0)
            return rawText;

        var rawSet = new HashSet<string>(rawWords.Select(w => w.Trim(',', '.', '!', '?', ';', ':').ToLowerInvariant()));
        var overlap = resultWords.Count(w => rawSet.Contains(w.Trim(',', '.', '!', '?', ';', ':').ToLowerInvariant()));
        if (overlap == 0 && rawWords.Length > 0)
            return rawText;

        if (rawWords.Length > 0)
        {
            var preservationRatio = (double)overlap / rawWords.Length;
            if (preservationRatio < 0.4)
                return rawText;
        }

        var explanatoryPatterns = new[] { "1.", "2.", "3.", "4.", "5.", "* ", "- ", "**", "##", "###" };
        if (explanatoryPatterns.Any(p => result.Contains(p, StringComparison.Ordinal)))
            return rawText;

        return result;
    }

    internal static string StripOutputWrappers(string text, string rawText)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        text = text.Replace("[DICTATED_TEXT_START]", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("[DICTATED_TEXT_END]", "", StringComparison.OrdinalIgnoreCase)
                   .Trim();

        var lines = text.Split('\n');
        var first = lines[0].Trim();

        var prefixes = new[]
        {
            "Here is the cleaned text:",
            "Here is the formatted text:",
            "Cleaned text:",
            "Formatted text:",
            "Result:",
            "Output:",
            "The formatted text is:",
            "The cleaned text is:",
            "Fixed text:",
        };

        foreach (var prefix in prefixes)
        {
            if (first.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                lines[0] = first[prefix.Length..].Trim();
                break;
            }
        }

        // Strip trailing empty or punctuation-only lines
        while (lines.Length > 0)
        {
            var last = lines[^1].Trim();
            if (string.IsNullOrWhiteSpace(last) ||
                last is "..." or "…" or "-" or "*" or "`" or "'" or "\"")
            {
                lines = lines[..^1];
            }
            else if (last.StartsWith("---", StringComparison.Ordinal) ||
                     last.StartsWith("```", StringComparison.Ordinal) ||
                     last.StartsWith("***", StringComparison.Ordinal))
            {
                lines = lines[..^1];
            }
            else
            {
                break;
            }
        }

        // Strip common trailing suffixes from the last non-empty line
        if (lines.Length > 0)
        {
            var lastLine = lines[^1].Trim();
            var suffixes = new[]
            {
                "Is there anything else I can help you with?",
                "Let me know if you need anything else.",
                "Can I help you with anything else?",
                "Do you need any further assistance?",
                "Do you need anything else?",
                "Anything else?",
                "Need help with anything else?",
            };

            foreach (var suffix in suffixes.OrderByDescending(s => s.Length))
            {
                if (lastLine.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    lastLine = lastLine[..^suffix.Length].TrimEnd();
                    lines[^1] = lastLine;
                    break;
                }
            }
        }

        var result = string.Join("\n", lines).Trim();
        if ((result.StartsWith('"') && result.EndsWith('"')) ||
            (result.StartsWith('\'') && result.EndsWith('\'')) ||
            (result.StartsWith('`') && result.EndsWith('`')))
        {
            if (result.Length > 2)
                result = result[1..^1].Trim();
        }

        // Strip trailing ellipsis/continuation markers unless raw text also ends with them
        var rawTrimmed = rawText.TrimEnd();
        if ((result.EndsWith("...") || result.EndsWith("…")) &&
            !(rawTrimmed.EndsWith("...") || rawTrimmed.EndsWith("…")))
        {
            result = result.TrimEnd('.', '…').TrimEnd();
        }

        return result;
    }

    /// <summary>
    /// Walks backwards from the end of both texts to find where the model appended extra content.
    /// If the result has trailing words not present in the raw text, and they look like artifacts
    /// (contain ellipsis, question marks, or are very short), they are stripped.
    /// </summary>
    internal static string StripTrailingArtifactsByRawAlignment(string result, string rawText)
    {
        if (string.IsNullOrWhiteSpace(result) || string.IsNullOrWhiteSpace(rawText))
            return result;

        var resultWords = result.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var rawWords = rawText.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (resultWords.Length <= rawWords.Length)
            return result;

        // Walk backwards to find how many trailing words in result match raw text
        int rawIdx = rawWords.Length - 1;
        int resultIdx = resultWords.Length - 1;
        int matchedTrailingWords = 0;

        while (rawIdx >= 0 && resultIdx >= 0)
        {
            var rw = rawWords[rawIdx].Trim(',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '…', '`', '*').ToLowerInvariant();
            var res = resultWords[resultIdx].Trim(',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '…', '`', '*').ToLowerInvariant();

            if (rw == res)
            {
                matchedTrailingWords++;
                rawIdx--;
                resultIdx--;
            }
            else
            {
                break;
            }
        }

        var unmatchedCount = resultWords.Length - 1 - resultIdx;
        if (unmatchedCount > 0 && unmatchedCount <= 6)
        {
            var trailingWords = resultWords[(resultIdx + 1)..];
            var trailingFragment = string.Join(" ", trailingWords);

            // If trailing fragment contains ellipsis, question mark, or looks like meta-commentary, strip it
            bool looksLikeArtifact = trailingFragment.Contains("...", StringComparison.Ordinal) ||
                                     trailingFragment.Contains("…", StringComparison.Ordinal) ||
                                     trailingFragment.Contains("?", StringComparison.Ordinal) ||
                                     (trailingFragment.Contains("!", StringComparison.Ordinal) && !rawText.TrimEnd().EndsWith('!')) ||
                                     trailingWords.Any(w => w.StartsWith('<') && w.EndsWith('>')); // token leakage like <|im_end|>

            if (looksLikeArtifact)
            {
                // Strip the trailing artifact from the end of the result string
                var searchFragment = string.Join(" ", trailingWords);
                var trimmedResult = result.TrimEnd();
                if (trimmedResult.EndsWith(searchFragment, StringComparison.OrdinalIgnoreCase))
                {
                    result = trimmedResult[..^searchFragment.Length].TrimEnd();
                }
            }
        }

        return result;
    }
}
