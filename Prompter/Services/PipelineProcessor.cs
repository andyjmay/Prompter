using Prompter.Models;

namespace Prompter.Services;

public class PipelineProcessor : IPipelineProcessor
{
    private readonly IModelManager _modelManager;
    private readonly ITranscriptionService _transcriptionService;
    private readonly ITextFormatter _textFormatter;
    private readonly IConfigService _configService;
    private readonly ISnippetMatcher _snippetMatcher;
    private readonly IFileLogger _logger;

    public PipelineProcessor(
        IModelManager modelManager,
        ITranscriptionService transcriptionService,
        ITextFormatter textFormatter,
        IConfigService configService,
        ISnippetMatcher snippetMatcher,
        IFileLogger logger)
    {
        _modelManager = modelManager;
        _transcriptionService = transcriptionService;
        _textFormatter = textFormatter;
        _configService = configService;
        _snippetMatcher = snippetMatcher;
        _logger = logger;
    }

    public async Task<PipelineResult> ProcessAsync(string wavPath, string modeId, CancellationToken ct = default, IProgress<string>? progress = null)
    {
        var cfg = _configService.Load();
        var mode = cfg.Modes.FirstOrDefault(m => m.Id.Equals(modeId, StringComparison.OrdinalIgnoreCase));

        _logger.Log("Ensuring models are loaded...");
        await _modelManager.EnsureModelsLoadedAsync();

        var configuredAlias = string.IsNullOrWhiteSpace(cfg.ChatModelId)
            ? ModelCatalog.DefaultChatAlias
            : cfg.ChatModelId;
        if (_modelManager.ChatReady && _modelManager.LoadedChatModelAlias != configuredAlias)
        {
            var loadedAlias = _modelManager.LoadedChatModelAlias!;
            _logger.Log($"Chat model fallback active. Configured: {configuredAlias}, Loaded: {loadedAlias}.");
        }

        progress?.Report("Transcribing…");
        var rawText = await _transcriptionService.TranscribeAsync(wavPath, cfg.Language, ct);
        rawText = rawText.Trim();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            _logger.Log("Transcription empty.");
            return new PipelineResult("", "", null, null, false, _modelManager.LoadedWhisperModelAlias ?? "unknown", null);
        }

        var matchedSnippet = _snippetMatcher.Match(rawText, cfg.Snippets);
        if (matchedSnippet != null)
        {
            _logger.Log($"Snippet matched: '{matchedSnippet.Trigger}' — returning expansion.");
            return new PipelineResult(
                matchedSnippet.Expansion,
                rawText,
                null,
                matchedSnippet.Trigger,
                false,
                _modelManager.LoadedWhisperModelAlias ?? "unknown",
                null);
        }

        var trueRawText = rawText;
        if (cfg.DictionaryEntries.Count > 0)
        {
            rawText = PersonalDictionaryProcessor.Process(rawText, cfg.DictionaryEntries, _logger);
        }
        if (cfg.SpokenPunctuationEnabled)
        {
            rawText = SpokenPunctuationProcessor.Process(rawText, cfg.Language, _logger);
        }
        _logger.Log($"Transcription complete using Whisper model '{_modelManager.LoadedWhisperModelAlias ?? "unknown"}'.");

        string finalText;
        string? formattedText = null;
        bool usedFallback = false;

        if (mode?.ShowDiagnosticOutput == true)
        {
            string diagFormattedText;
            string statusLine;

            if (_modelManager.ChatReady && mode?.SkipFormatting != true)
            {
                try
                {
                    progress?.Report("Formatting…");
                    diagFormattedText = await _textFormatter.CleanupAsync(rawText, modeId, ct);
                    statusLine = "Chat model: " + (_modelManager.LoadedChatModelAlias ?? "unknown");
                }
                catch (Exception ex)
                {
                    _logger.LogException(ex, "CleanupAsync failed in diagnostic mode");
                    diagFormattedText = rawText;
                    statusLine = "Chat model failed — raw text shown below";
                }
            }
            else
            {
                diagFormattedText = rawText;
                statusLine = mode?.SkipFormatting == true
                    ? "Formatting skipped — raw text shown below"
                    : "Chat model not loaded — raw text shown below";
            }

            finalText = $"[RAW]{Environment.NewLine}{trueRawText}{Environment.NewLine}{Environment.NewLine}[FORMATTED]{Environment.NewLine}{diagFormattedText}{Environment.NewLine}{Environment.NewLine}[STATUS]{Environment.NewLine}{statusLine}";
            formattedText = diagFormattedText;
        }
        else if (mode?.SkipFormatting == true || !_modelManager.ChatReady)
        {
            if (mode?.SkipFormatting != true)
            {
                _logger.Log("Chat model not ready — falling back to Raw.");
                usedFallback = true;
            }
            finalText = rawText;
        }
        else
        {
            try
            {
                progress?.Report("Formatting…");
                formattedText = await _textFormatter.CleanupAsync(rawText, modeId, ct);
                finalText = formattedText;
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "CleanupAsync failed — falling back to raw transcription");
                finalText = rawText;
                usedFallback = true;
            }
        }

        return new PipelineResult(
            finalText,
            trueRawText,
            formattedText,
            null,
            usedFallback,
            _modelManager.LoadedWhisperModelAlias ?? "unknown",
            _modelManager.LoadedChatModelAlias);
    }
}
