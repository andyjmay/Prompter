using System.Text;
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

    private const string ListFormattingPrompt =
        "You rewrite dictated text to format any spoken lists into structured markdown lists. " +
        "Follow these rules:\n" +
        "1. Use \"- \" for unordered bullet lists.\n" +
        "2. Use \"1. \", \"2. \", etc., for ordered lists.\n" +
        "3. Use \"- [ ] \" for task lists if the speaker implies checkboxes, to-do items, or action items (e.g. \"todo: call John\", \"action items\").\n" +
        "4. Preserve indentation (using 4 spaces per level) for nested lists when the speaker says \"sub-item\", \"under that\", or implies nesting.\n" +
        "5. If the input does not contain a list, format it normally (fixing typos, capitalization, and missing punctuation) without list markers.\n" +
        "6. Do not rewrite sentences, change meaning, write replies, or add trailing commentary/ellipsis. Output ONLY the corrected text.";

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

        bool listFormattingActive = cfg.ListFormattingEnabled && !mode.SkipFormatting;
        if (listFormattingActive)
        {
            systemPrompt += "\n\n" + ListFormattingPrompt;
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

        bool flexibleFormatting = cleaningActive || listFormattingActive;
        var userMessage = flexibleFormatting
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
        result = RejectIfHallucinated(rawText, result, modeId, cleaningActive, listFormattingActive);
        result = StripSpecialTokens(result);

        if (cleaningActive)
        {
            var protectedWords = new HashSet<string>(validWords.Distinct(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            result = StripFillers(result, protectedWords);
        }

        if (listFormattingActive)
        {
            result = ApplyListFormattingSafetyNet(rawText, result);
            result = FormatListSpacing(result);
        }

        if (modeId.Equals(ModeDefaults.CodeId, StringComparison.OrdinalIgnoreCase))
        {
            result = ApplyCodeModeSafeguards(result);
        }

        _fileLogger.Log($"Chat model '{_modelManager.LoadedChatModelAlias ?? "unknown"}' cleaned text: {result}");
        return result;
    }

    internal static string RejectIfHallucinated(string rawText, string result, string? modeId = null, bool cleanEnabled = false, bool listFormattingEnabled = false)
    {
        if (string.IsNullOrWhiteSpace(result)) return rawText;

        var comparisonResult = listFormattingEnabled ? result.Replace('\n', ' ').Replace('\r', ' ') : result;
        var rawWords = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var resultWords = comparisonResult.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (resultWords.Length > rawWords.Length * 2 && rawWords.Length > 0)
            return rawText;

        var rawSet = new HashSet<string>(rawWords.Select(w => w.Trim(',', '.', '!', '?', ';', ':').ToLowerInvariant()));
        var overlap = resultWords.Count(w => IsCloseMatch(w, rawSet));
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
            var minRatio = modeId?.Equals(ModeDefaults.CodeId, StringComparison.OrdinalIgnoreCase) == true ? 0.15 : 0.4;
            if (preservationRatio < minRatio)
                return rawText;
        }

        if (!listFormattingEnabled)
        {
            var explanatoryPatterns = new[] { "1.", "2.", "3.", "4.", "5.", "* ", "- ", "**", "##", "###" };
            if (explanatoryPatterns.Any(p => result.Contains(p, StringComparison.Ordinal)))
                return rawText;
        }

        return result;
    }

    internal static bool IsCloseMatch(string word, HashSet<string> rawSet)
    {
        var clean = word.Trim(',', '.', '!', '?', ';', ':').ToLowerInvariant();
        if (rawSet.Contains(clean))
            return true;

        foreach (var rawWord in rawSet)
        {
            if (HasCommonSubstring(clean, rawWord, 3))
                return true;
            if (LevenshteinDistance(clean, rawWord) <= 2)
                return true;
        }
        return false;
    }

    private static bool HasCommonSubstring(string a, string b, int minLength)
    {
        if (a.Length < minLength || b.Length < minLength)
            return false;

        for (int i = 0; i <= a.Length - minLength; i++)
        {
            for (int j = 0; j <= b.Length - minLength; j++)
            {
                bool match = true;
                for (int k = 0; k < minLength; k++)
                {
                    if (a[i + k] != b[j + k])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return true;
            }
        }
        return false;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            Array.Copy(current, previous, b.Length + 1);
        }

        return previous[b.Length];
    }

    internal static string StripOutputWrappers(string text, string rawText)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        text = text.Replace("[DICTATED_TEXT_START]", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("[DICTATED_TEXT_END]", "", StringComparison.OrdinalIgnoreCase)
                   .Trim();

        var lines = text.Split('\n');
        var first = lines[0].Trim();

        // Strip leading markdown code fence line (e.g. ``` or ```text)
        if (first.Length >= 3 && first.TakeWhile(c => c == '`').Count() >= 3)
        {
            if (lines.Length > 1)
            {
                lines = lines[1..];
            }
            else
            {
                lines[0] = first.TrimStart('`').TrimStart();
            }
        }

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

        // Strip all layers of surrounding quotes or backticks
        while (result.Length > 2 &&
               ((result[0] == '"' && result[^1] == '"') ||
                (result[0] == '\'' && result[^1] == '\'') ||
                (result[0] == '`' && result[^1] == '`')))
        {
            result = result[1..^1].Trim();
        }

        // Strip trailing attached backticks even if text doesn't start with them
        if (result.Length > 0 && result[^1] == '`')
        {
            int trailingBackticks = 0;
            for (int i = result.Length - 1; i >= 0 && result[i] == '`'; i--)
                trailingBackticks++;
            if (trailingBackticks >= 3)
            {
                result = result[..^trailingBackticks].TrimEnd();
            }
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

        // Bug fix: if the very last word didn't match, resultIdx never moved, making unmatchedCount == 0.
        // In that case we still want to examine the trailing words for artifacts, but only the extra
        // words beyond the raw text length to avoid slicing into the entire short result.
        int unmatchedCount;
        if (matchedTrailingWords == 0)
        {
            unmatchedCount = Math.Min(6, Math.Max(0, resultWords.Length - rawWords.Length));
            resultIdx = Math.Max(-1, resultWords.Length - unmatchedCount - 1);
        }
        else
        {
            unmatchedCount = resultWords.Length - 1 - resultIdx;
        }

        if (unmatchedCount > 0 && unmatchedCount <= 6)
        {
            var trailingWords = resultWords[(resultIdx + 1)..];
            var trailingFragment = string.Join(" ", trailingWords);

            // If trailing fragment contains ellipsis, question mark, or looks like meta-commentary, strip it
            bool looksLikeArtifact = trailingFragment.Contains("...", StringComparison.Ordinal) ||
                                     trailingFragment.Contains("…", StringComparison.Ordinal) ||
                                     trailingFragment.Contains("?", StringComparison.Ordinal) ||
                                     (trailingFragment.Contains("!", StringComparison.Ordinal) && !rawText.TrimEnd().EndsWith('!')) ||
                                     trailingWords.Any(w => w.StartsWith('<') && w.EndsWith('>')); // token leakage like  

            if (looksLikeArtifact)
            {
                // Strip the trailing artifact from the end of the result string
                var searchFragment = string.Join(" ", trailingWords);
                var trimmedResult = result.TrimEnd();
                if (EndsWithIgnoringTrailingPunctuation(trimmedResult, searchFragment))
                {
                    result = trimmedResult[..^searchFragment.Length].TrimEnd();
                }
            }
        }

        return result;
    }

    private static readonly char[] TrailingPunctuationChars = new[] { '.', ',', '!', '?', ';', ':', '…', '`', '*', '"', '\'', '(', ')' };

    private static bool EndsWithIgnoringTrailingPunctuation(string text, string fragment)
    {
        if (text.Length < fragment.Length)
            return false;

        var strippedText = text.TrimEnd(TrailingPunctuationChars);
        var strippedFragment = fragment.TrimEnd(TrailingPunctuationChars);

        if (strippedText.Length < strippedFragment.Length)
            return false;

        return strippedText.EndsWith(strippedFragment, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex SpecialTokenRegex = new(
        @"<\|(?:im_end|im_start|endoftext|eot_id|assistant|user|system|system_message| ToolCalls|finish_reason|stop|\w+)\|>" +
        @"|</s>|\[DICTATED_TEXT_(?:START|END)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static string StripSpecialTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = SpecialTokenRegex.Replace(text, "");
        // Collapse any leftover whitespace/newlines created by the removal
        text = Regex.Replace(text, @"[ \t]*\r?\n[ \t\r\n]*", "\n");
        text = Regex.Replace(text, @"  +", " ");
        return text.Trim();
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

    internal static string ApplyListFormattingSafetyNet(string rawText, string result)
    {
        if (string.IsNullOrWhiteSpace(rawText) || string.IsNullOrWhiteSpace(result))
            return result;

        var hasNumericSequence = (Regex.IsMatch(rawText, @"\b1\.\s") && Regex.IsMatch(rawText, @"\b2\.\s")) ||
                                 (Regex.IsMatch(rawText, @"\b1\)\s") && Regex.IsMatch(rawText, @"\b2\)\s"));
        if (!hasNumericSequence)
        {
            return result;
        }

        var pattern = @"(?<![\r\n])\b(?<num>\d+)[\.\)]\s";
        var matches = Regex.Matches(result, pattern);
        if (matches.Count > 0)
        {
            var sb = new StringBuilder();
            int lastIndex = 0;
            bool isFirst = true;

            foreach (Match m in matches)
            {
                var segment = result.Substring(lastIndex, m.Index - lastIndex);
                sb.Append(segment);

                if (isFirst)
                {
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        while (sb.Length > 0 && char.IsWhiteSpace(sb[^1]))
                        {
                            sb.Length--;
                        }
                        sb.Append("\n\n");
                    }
                    isFirst = false;
                }
                else
                {
                    while (sb.Length > 0 && char.IsWhiteSpace(sb[^1]))
                    {
                        sb.Length--;
                    }
                    sb.Append("\n");
                }

                sb.Append(m.Value);
                lastIndex = m.Index + m.Length;
            }
            sb.Append(result.Substring(lastIndex));
            result = sb.ToString();
        }

        return result;
    }

    internal static string FormatListSpacing(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var resultLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var currentLine = lines[i].TrimEnd();
            var trimmedStart = currentLine.TrimStart();
            
            if (string.IsNullOrWhiteSpace(currentLine))
            {
                int nextIdx = i + 1;
                while (nextIdx < lines.Length && string.IsNullOrWhiteSpace(lines[nextIdx]))
                {
                    nextIdx++;
                }
                if (nextIdx < lines.Length)
                {
                    var nextTrimmed = lines[nextIdx].TrimStart();
                    if (IsListItem(nextTrimmed))
                    {
                        continue;
                    }
                }
            }

            bool isCurrentList = IsListItem(trimmedStart);

            if (resultLines.Count > 0)
            {
                var prevLine = resultLines[^1];
                var prevTrimmed = prevLine.TrimStart();
                bool isPrevList = IsListItem(prevTrimmed);
                bool isPrevEmpty = string.IsNullOrWhiteSpace(prevLine);

                if (isCurrentList)
                {
                    if (isPrevList)
                    {
                        // Single newline: do nothing
                    }
                    else if (!isPrevEmpty)
                    {
                        // List starting after text: ensure double newline
                        resultLines.Add("");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(currentLine))
                {
                    if (isPrevList)
                    {
                        // Text starting after list: ensure double newline
                        resultLines.Add("");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(currentLine))
            {
                if (resultLines.Count == 0 || string.IsNullOrWhiteSpace(resultLines[^1]))
                {
                    continue;
                }
            }

            resultLines.Add(currentLine);
        }

        while (resultLines.Count > 0 && string.IsNullOrWhiteSpace(resultLines[^1]))
        {
            resultLines.RemoveAt(resultLines.Count - 1);
        }

        return string.Join("\n", resultLines);
    }

    private static bool IsListItem(string trimmedLine)
    {
        if (string.IsNullOrEmpty(trimmedLine)) return false;

        if ((trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* ")) && trimmedLine.Length > 2)
            return true;

        if (trimmedLine.StartsWith("- [ ]") || trimmedLine.StartsWith("- [x]") || trimmedLine.StartsWith("- [X]") ||
            trimmedLine.StartsWith("* [ ]") || trimmedLine.StartsWith("* [x]") || trimmedLine.StartsWith("* [X]"))
            return true;

        var match = Regex.Match(trimmedLine, @"^[1-9]\d{0,2}[\.\)]\s");
        return match.Success;
    }

    internal static string ApplyCodeModeSafeguards(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // 1. Restore CLI flags: convert "dash [letter or number]" to "-[letter or number]"
        // Also convert double dash: "dash dash [word]" or "double dash [word]" to "--[word]"
        text = Regex.Replace(text, @"\bdash\s+([a-zA-Z0-9])\b", "-$1", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(?:dash\s+dash|double\s+dash)\s+([a-zA-Z0-9]+)\b", "--$1", RegexOptions.IgnoreCase);

        // 2. Restore verbalized dots between alphanumeric characters / underscores
        text = Regex.Replace(text, @"(?<=[a-zA-Z0-9_-])\s*\bdot\b\s*(?=[a-zA-Z0-9_-])", ".");

        // 3. Collapse spaces around dots in potential file paths ending in protected extensions
        var extensions = "ts|js|tsx|jsx|py|cs|json|html|css|yml|yaml|md|sh|bat|rs|go|cpp|h|java|kt|sql|ini|conf|toml";
        var extPattern = $@"\b([a-zA-Z0-9_-]+(?:\s*\.\s*[a-zA-Z0-9_-]+)*)\s*\.\s*({extensions})\b";
        text = Regex.Replace(text, extPattern, m => Regex.Replace(m.Value, @"\s+", ""), RegexOptions.IgnoreCase);

        // 4. Collapse spaces in multi-character operators
        text = Regex.Replace(text, @"=\s+=\s+=", "===");
        text = Regex.Replace(text, @"=\s+=", "==");
        text = Regex.Replace(text, @"!\s+=\s+=", "!==");
        text = Regex.Replace(text, @"!\s+=", "!=");
        text = Regex.Replace(text, @"=\s+>", "=>");
        text = Regex.Replace(text, @"&\s+&", "&&");
        text = Regex.Replace(text, @"\|\s+\|", "||");
        text = Regex.Replace(text, @"<\s+=", "<=");
        text = Regex.Replace(text, @">\s+=", ">=");
        text = Regex.Replace(text, @"\+\s+=", "+=");
        text = Regex.Replace(text, @"-\s+=", "-=");

        return text;
    }
}
