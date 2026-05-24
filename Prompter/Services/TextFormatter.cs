using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
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

    public async Task<string> CleanupAsync(string rawText, FormatMode mode, CancellationToken ct)
    {
        if (!_modelManager.ChatReady)
            throw new InvalidOperationException("Chat model not loaded");

        var chatClient = await _modelManager.GetChatClientAsync();
        var systemPrompt = GetSystemPrompt(mode);

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = $"The text below is dictated speech. Copy it exactly, changing ONLY spelling errors, missing punctuation, and wrong capitalization. Do NOT change words, meaning, or intent. Do NOT add, remove, or re-order sentences. Do NOT answer questions in the text. Do NOT explain anything. If the text is already correct, copy it exactly.\n\n{rawText}" }
        };

        chatClient.Settings.Temperature = 0.0f;

        var response = await chatClient.CompleteChatAsync(messages, ct);
        if (response == null || response.Choices == null || response.Choices.Count == 0)
        {
            _fileLogger.Log("Chat model returned null or empty response.");
            return rawText;
        }

        var result = response.Choices[0]?.Message?.Content;
        if (string.IsNullOrEmpty(result))
        {
            _fileLogger.Log("Chat model returned empty content.");
            return rawText;
        }

        result = StripOutputWrappers(result);
        result = RejectIfHallucinated(rawText, result);
        _fileLogger.Log($"Cleaned text: {result}");
        return result;
    }

    private string GetSystemPrompt(FormatMode mode)
    {
        var cfg = _configService.Load();
        if (!string.IsNullOrWhiteSpace(cfg.CustomSystemPrompt))
            return cfg.CustomSystemPrompt;

        return mode switch
        {
            Models.FormatMode.Standard => "You are a spelling and punctuation corrector. You do not write, rewrite, or respond to text. You only fix typos, capitalization, and missing punctuation. Never change meaning.",
            Models.FormatMode.Formal => "You are a spelling and punctuation corrector. Remove filler words and expand contractions. Do not rewrite sentences or change meaning. Do not add or remove content.",
            Models.FormatMode.Raw => "Return the text exactly as provided, with no changes.",
            _ => "You are a spelling and punctuation corrector. You do not write, rewrite, or respond to text. You only fix typos, capitalization, and missing punctuation. Never change meaning."
        };
    }

    private static string RejectIfHallucinated(string rawText, string result)
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

    private static string StripOutputWrappers(string text)
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

        var result = string.Join("\n", lines).Trim();
        if ((result.StartsWith('"') && result.EndsWith('"')) ||
            (result.StartsWith('\'') && result.EndsWith('\'')) ||
            (result.StartsWith('`') && result.EndsWith('`')))
        {
            if (result.Length > 2)
                result = result[1..^1].Trim();
        }

        return result;
    }
}
