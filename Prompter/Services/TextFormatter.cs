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

    // Source of truth for all filler words/phrases used in Clean mode
    private static readonly string[] UnambiguousFillers = new[] { "you know", "i mean", "sort of", "kind of", "um", "uh" };
    private static readonly string[] ContextualFillers = new[] { "like", "basically", "literally" };

    public async Task<string> CleanupAsync(string rawText, string modeId, CancellationToken ct)
    {
        if (!_modelManager.ChatReady)
            throw new InvalidOperationException("Chat model not loaded");

        var cfg = _configService.Load();
        var mode = cfg.Modes.FirstOrDefault(m => m.Id.Equals(modeId, StringComparison.OrdinalIgnoreCase)) ?? ModeDefaults.Standard;
        var systemPrompt = mode.SystemPrompt;

        bool cleaningActive = cfg.CleanEnabled && !mode.SkipFormatting;
        if (cleaningActive)
        {
            var cleanInstruction = cfg.CleanPrompt;
            if (!string.IsNullOrWhiteSpace(cleanInstruction))
            {
                systemPrompt += "\n\n" + cleanInstruction;
            }
        }

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

        var userMessage = cleaningActive
            ? $"The text below is dictated speech. Apply the system instructions. Do NOT add, remove, or re-order sentences. Do NOT answer questions in the text. Do NOT explain anything. Do NOT add trailing ellipsis, continuation markers, or commentary. Output ONLY the corrected text. If the text is already correct, copy it exactly.\n\n{rawText}"
            : $"The text below is dictated speech. Copy it exactly, changing ONLY spelling errors, missing punctuation, and wrong capitalization. Do NOT change words, meaning, or intent. Do NOT add, remove, or re-order sentences. Do NOT answer questions in the text. Do NOT explain anything. Do NOT add trailing ellipsis, continuation markers, or commentary. Output ONLY the corrected text. If the text is already correct, copy it exactly.\n\n{rawText}";

        var messages = new List<ChatMessage>
        {
            new("system", systemPrompt),
            new("user", userMessage)
        };

        _fileLogger.Log($"Formatting with chat model '{_modelManager.LoadedChatModelAlias ?? "unknown"}' in '{mode.Name}' mode.");
        var result = await chatClient.CompleteAsync(messages, 0.0f, ct);
        if (string.IsNullOrEmpty(result))
        {
            _fileLogger.Log("Chat model returned null or empty response.");
            return rawText;
        }

        result = StripOutputWrappers(result, rawText);
        result = StripTrailingArtifactsByRawAlignment(result, rawText);
        result = RejectIfHallucinated(rawText, result, modeId, cleaningActive);

        if (cleaningActive)
        {
            var protectedWords = new HashSet<string>(validWords.Distinct(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            result = StripFillers(result, protectedWords);
        }

        _fileLogger.Log($"Chat model '{_modelManager.LoadedChatModelAlias ?? "unknown"}' cleaned text: {result}");
        return result;
    }

    internal static string RejectIfHallucinated(string rawText, string result, string? modeId = null, bool cleanEnabled = false)
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

        var denominator = rawWords.Length;
        if (cleanEnabled)
        {
            var singleWordFillers = UnambiguousFillers.Where(f => !f.Contains(' '))
                .Concat(ContextualFillers)
                .ToHashSet();
            var multiWordFillers = UnambiguousFillers.Where(f => f.Contains(' '))
                .Select(f => f.Split(' '))
                .ToArray();

            var isFillerWord = new bool[rawWords.Length];

            for (int i = 0; i < rawWords.Length; i++)
            {
                var clean = rawWords[i].Trim(',', '.', '!', '?', ';', ':').ToLowerInvariant();
                if (singleWordFillers.Contains(clean))
                    isFillerWord[i] = true;
            }

            foreach (var phrase in multiWordFillers)
            {
                for (int i = 0; i <= rawWords.Length - phrase.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < phrase.Length; j++)
                    {
                        var clean = rawWords[i + j].Trim(',', '.', '!', '?', ';', ':').ToLowerInvariant();
                        if (clean != phrase[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        for (int j = 0; j < phrase.Length; j++)
                            isFillerWord[i + j] = true;
                    }
                }
            }

            denominator = rawWords.Length - isFillerWord.Count(static b => b);
            if (denominator == 0) denominator = 1;
        }

        if (denominator > 0)
        {
            var preservationRatio = (double)overlap / denominator;
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

    internal static string StripFillers(string text, HashSet<string> protectedWords)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var activeUnambiguous = UnambiguousFillers.Where(f => !protectedWords.Contains(f)).ToList();
        var activeContextual = ContextualFillers.Where(f => !protectedWords.Contains(f)).ToList();

        if (activeUnambiguous.Count > 0)
        {
            var escaped = activeUnambiguous.OrderByDescending(f => f.Length).Select(f => Regex.Escape(f));
            var pattern = $@"\b({string.Join("|", escaped)})\b";
            text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase);
        }

        if (activeContextual.Count > 0)
        {
            var escaped = activeContextual.Select(f => Regex.Escape(f));
            var joined = string.Join("|", escaped);
            var pattern = $@"(?<![\w'-])({joined})(?=\s*[,.:;])|(?<=[,;:\-]\s*)({joined})(?=\s*[,.:;]|$)";
            text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase);
        }

        text = Regex.Replace(text, @",\s*,", ",");           // collapse double commas
        text = Regex.Replace(text, @",\s*([.!?])", "$1");    // comma before end punctuation
        text = Regex.Replace(text, @"\s+,", ",");            // remove space before comma
        text = Regex.Replace(text, @"\s+([.!?])", "$1");     // remove space before end punctuation
        text = Regex.Replace(text, @"\s+", " ");             // collapse multiple spaces
        text = text.Trim();
        text = text.Trim(',');                               // remove dangling commas
        text = text.Trim();

        return text;
    }
}
