using Prompter.Models;
using Prompter.Services;
using Prompter.Tests.Fakes;
using Xunit;

namespace Prompter.Tests.Services;

public class PipelineProcessorTests
{
    private readonly FakeModelManager _modelManager;
    private readonly FakeTranscriptionService _transcriptionService;
    private readonly FakeTextFormatter _textFormatter;
    private readonly FakeConfigService _configService;
    private readonly SnippetMatcher _snippetMatcher;
    private readonly FakeFileLogger _logger;
    private readonly PipelineProcessor _processor;

    public PipelineProcessorTests()
    {
        _modelManager = new FakeModelManager();
        _transcriptionService = new FakeTranscriptionService();
        _textFormatter = new FakeTextFormatter();
        _configService = new FakeConfigService(new AppConfig());
        _snippetMatcher = new SnippetMatcher();
        _logger = new FakeFileLogger();

        _processor = new PipelineProcessor(
            _modelManager,
            _transcriptionService,
            _textFormatter,
            _configService,
            _snippetMatcher,
            _logger);
    }

    [Fact]
    public async Task ProcessAsync_EmptyTranscription_ReturnsEmptyResult()
    {
        _transcriptionService.FixedResult = "   ";

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        Assert.Equal("", result.FinalText);
        Assert.Equal("", result.RawText);
        Assert.Null(result.FormattedText);
        Assert.Null(result.MatchedSnippetTrigger);
    }

    [Fact]
    public async Task ProcessAsync_SnippetMatched_ReturnsSnippetExpansionDirectly()
    {
        var config = new AppConfig();
        config.Snippets.Add(new Snippet { Trigger = "br", Expansion = "Best regards" });
        await _configService.SaveAsync(config);

        _transcriptionService.FixedResult = "br";

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        Assert.Equal("Best regards", result.FinalText);
        Assert.Equal("br", result.RawText);
        Assert.Null(result.FormattedText);
        Assert.Equal("br", result.MatchedSnippetTrigger);
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulFormatting_ReturnsFormattedText()
    {
        _transcriptionService.FixedResult = "raw text";
        _textFormatter.FixedResult = "formatted text";

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        Assert.Equal("formatted text", result.FinalText);
        Assert.Equal("raw text", result.RawText);
        Assert.Equal("formatted text", result.FormattedText);
        Assert.False(result.UsedFormattingFallback);
    }

    [Fact]
    public async Task ProcessAsync_FormattingThrowsException_FallsBackToRawText()
    {
        _transcriptionService.FixedResult = "raw text";
        _textFormatter.ThrowException = new InvalidOperationException("formatting failed");

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        Assert.Equal("raw text", result.FinalText);
        Assert.Equal("raw text", result.RawText);
        Assert.Null(result.FormattedText);
        Assert.True(result.UsedFormattingFallback);
    }

    [Fact]
    public async Task ProcessAsync_ChatNotReady_FallsBackToRawText()
    {
        _transcriptionService.FixedResult = "raw text";
        _modelManager.ChatReady = false;

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        Assert.Equal("raw text", result.FinalText);
        Assert.Equal("raw text", result.RawText);
        Assert.Null(result.FormattedText);
        Assert.True(result.UsedFormattingFallback);
    }

    [Fact]
    public async Task ProcessAsync_SkipFormatting_BypassesFormatter()
    {
        var config = new AppConfig();
        var rawMode = config.Modes.First(m => m.Id == "raw");
        Assert.True(rawMode.SkipFormatting);
        await _configService.SaveAsync(config);

        _transcriptionService.FixedResult = "raw text";
        _textFormatter.FixedResult = "formatted text"; // Should not be used

        var result = await _processor.ProcessAsync("dummy.wav", "raw");

        Assert.Equal("raw text", result.FinalText);
        Assert.Equal("raw text", result.RawText);
        Assert.Null(result.FormattedText);
        Assert.False(result.UsedFormattingFallback);
    }

    [Fact]
    public async Task ProcessAsync_DiagnosticMode_ReturnsRawAndFormattedAndStatus()
    {
        var config = new AppConfig();
        var mode = config.Modes.First(m => m.Id == "standard");
        // Create a custom mode configuration or update existing to have ShowDiagnosticOutput = true
        var diagnosticMode = mode with { ShowDiagnosticOutput = true };
        config.Modes.Remove(mode);
        config.Modes.Add(diagnosticMode);
        await _configService.SaveAsync(config);

        _transcriptionService.FixedResult = "raw text";
        _textFormatter.FixedResult = "formatted text";

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        var expectedStatus = "Chat model: " + _modelManager.LoadedChatModelAlias;
        var expectedFinal = $"[RAW]{Environment.NewLine}raw text{Environment.NewLine}{Environment.NewLine}[FORMATTED]{Environment.NewLine}formatted text{Environment.NewLine}{Environment.NewLine}[STATUS]{Environment.NewLine}{expectedStatus}";

        Assert.Equal(expectedFinal, result.FinalText);
        Assert.Equal("raw text", result.RawText);
        Assert.Equal("formatted text", result.FormattedText);
    }

    [Fact]
    public async Task ProcessAsync_DiagnosticMode_FormattingFails_ShowsErrorInStatus()
    {
        var config = new AppConfig();
        var mode = config.Modes.First(m => m.Id == "standard");
        var diagnosticMode = mode with { ShowDiagnosticOutput = true };
        config.Modes.Remove(mode);
        config.Modes.Add(diagnosticMode);
        await _configService.SaveAsync(config);

        _transcriptionService.FixedResult = "raw text";
        _textFormatter.ThrowException = new InvalidOperationException("formatter error");

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        var expectedStatus = "Chat model failed — raw text shown below";
        var expectedFinal = $"[RAW]{Environment.NewLine}raw text{Environment.NewLine}{Environment.NewLine}[FORMATTED]{Environment.NewLine}raw text{Environment.NewLine}{Environment.NewLine}[STATUS]{Environment.NewLine}{expectedStatus}";

        Assert.Equal(expectedFinal, result.FinalText);
        Assert.Equal("raw text", result.RawText);
        Assert.Equal("raw text", result.FormattedText); // fallback diagnostic text
    }

    [Fact]
    public async Task ProcessAsync_DiagnosticMode_ChatNotReady_ShowsChatNotReadyInStatus()
    {
        var config = new AppConfig();
        var mode = config.Modes.First(m => m.Id == "standard");
        var diagnosticMode = mode with { ShowDiagnosticOutput = true };
        config.Modes.Remove(mode);
        config.Modes.Add(diagnosticMode);
        await _configService.SaveAsync(config);

        _transcriptionService.FixedResult = "raw text";
        _modelManager.ChatReady = false;

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        var expectedStatus = "Chat model not loaded — raw text shown below";
        var expectedFinal = $"[RAW]{Environment.NewLine}raw text{Environment.NewLine}{Environment.NewLine}[FORMATTED]{Environment.NewLine}raw text{Environment.NewLine}{Environment.NewLine}[STATUS]{Environment.NewLine}{expectedStatus}";

        Assert.Equal(expectedFinal, result.FinalText);
        Assert.Equal("raw text", result.RawText);
        Assert.Equal("raw text", result.FormattedText);
    }

    [Fact]
    public async Task ProcessAsync_PassesCancellationToken_ToEnsureModelsLoadedAsync()
    {
        var cts = new CancellationTokenSource();
        _transcriptionService.FixedResult = "raw text";

        await _processor.ProcessAsync("dummy.wav", "standard", cts.Token);

        Assert.True(_modelManager.LastEnsureModelsToken.HasValue);
        Assert.Equal(cts.Token, _modelManager.LastEnsureModelsToken.Value);
    }

    [Fact]
    public async Task ProcessAsync_AppliesPersonalDictionary()
    {
        var config = new AppConfig();
        config.DictionaryEntries.Add(new DictionaryEntry { Word = "OpenAI", Aliases = new List<string> { "open ai" } });
        await _configService.SaveAsync(config);

        _transcriptionService.FixedResult = "we love open ai";
        
        // Formatter should receive the dictionary processed text "we love OpenAI"
        _textFormatter.OnCleanup = (raw, modeId) =>
        {
            Assert.Equal("we love OpenAI", raw);
            return Task.FromResult("formatted");
        };

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        Assert.Equal("formatted", result.FinalText);
    }

    [Fact]
    public async Task ProcessAsync_AppliesSpokenPunctuation()
    {
        var config = new AppConfig { SpokenPunctuationEnabled = true };
        await _configService.SaveAsync(config);

        _transcriptionService.FixedResult = "hello comma world period";

        _textFormatter.OnCleanup = (raw, modeId) =>
        {
            Assert.Equal("hello, world.", raw);
            return Task.FromResult("formatted");
        };

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        Assert.Equal("formatted", result.FinalText);
    }

    [Fact]
    public async Task ProcessAsync_UnknownModeId_ReturnsRawText()
    {
        _transcriptionService.FixedResult = "raw text";
        _textFormatter.FixedResult = "formatted text";

        var result = await _processor.ProcessAsync("dummy.wav", "nonexistent-mode");

        Assert.Equal("raw text", result.FinalText);
        Assert.Equal("raw text", result.RawText);
        Assert.Null(result.FormattedText);
        Assert.False(result.UsedFormattingFallback);
    }

    [Fact]
    public async Task ProcessAsync_NoneChatModel_SkipsFormatting()
    {
        var config = new AppConfig { ChatModelId = "none" };
        await _configService.SaveAsync(config);

        _transcriptionService.FixedResult = "raw text";
        _textFormatter.FixedResult = "formatted text";

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        Assert.Equal("raw text", result.FinalText);
        Assert.Equal("raw text", result.RawText);
        Assert.Null(result.FormattedText);
        Assert.False(result.UsedFormattingFallback);
    }

    [Fact]
    public async Task ProcessAsync_CombinedDictionaryAndSpokenPunctuation()
    {
        var config = new AppConfig { SpokenPunctuationEnabled = true };
        config.DictionaryEntries.Add(new DictionaryEntry { Word = "OpenAI", Aliases = new List<string> { "open ai" } });
        await _configService.SaveAsync(config);

        _transcriptionService.FixedResult = "we love open ai comma world period";

        _textFormatter.OnCleanup = (raw, modeId) =>
        {
            Assert.Equal("we love OpenAI, world.", raw);
            return Task.FromResult("formatted");
        };

        var result = await _processor.ProcessAsync("dummy.wav", "standard");

        Assert.Equal("formatted", result.FinalText);
    }

    [Fact]
    public async Task ProcessAsync_ReportsProgress()
    {
        _transcriptionService.FixedResult = "raw text";

        var reports = new List<string>();
        var progress = new Progress<string>(stage => reports.Add(stage));

        await _processor.ProcessAsync("dummy.wav", "standard", progress: progress);

        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task ProcessAsync_Cancellation_HonorsToken()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _processor.ProcessAsync("dummy.wav", "standard", cts.Token));
    }

    private class FakeTextFormatter : ITextFormatter
    {
        public string FixedResult { get; set; } = "";
        public Exception? ThrowException { get; set; }
        public Func<string, string, Task<string>>? OnCleanup { get; set; }

        public Task<string> CleanupAsync(string rawText, string modeId, CancellationToken ct)
        {
            if (ThrowException != null)
                throw ThrowException;

            if (OnCleanup != null)
                return OnCleanup(rawText, modeId);

            return Task.FromResult(FixedResult);
        }
    }
}
