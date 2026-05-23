using System.Threading;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;

namespace Prompter.Services;

public class FoundryOrchestrator : IDisposable
{
    private readonly FileLogger _fileLogger;
    private FoundryLocalManager? _manager;
    private Microsoft.AI.Foundry.Local.IModel? _whisperModel;
    private Microsoft.AI.Foundry.Local.IModel? _chatModel;
    private System.Timers.Timer? _idleTimer;
    private DateTime _lastUsed;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);

    public bool WhisperReady => _whisperModel != null;
    public bool ChatReady => _chatModel != null;

    public event Action<string, float>? ModelDownloadProgress;

    public FoundryOrchestrator(FileLogger fileLogger)
    {
        _fileLogger = fileLogger;
    }

    public async Task InitializeAsync(int idleTtlMinutes)
    {
        var config = new Configuration
        {
            AppName = "Prompter",
            LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Information
        };

        // We use a minimal wrapper logger because Foundry expects ILogger
        var factory = LoggerFactory.Create(b => b.AddProvider(new FileLogProvider(_fileLogger)));
        var logger = factory.CreateLogger<FoundryOrchestrator>();

        await FoundryLocalManager.CreateAsync(config, logger);
        _manager = FoundryLocalManager.Instance;

        if (_manager == null)
            throw new InvalidOperationException("FoundryLocalManager.Instance is null after CreateAsync.");

        _fileLogger.Log("Foundry Local manager initialized.");

        // Register execution providers
        try
        {
            await _manager.DownloadAndRegisterEpsAsync((ep, pct) =>
            {
                // silently
            });
        }
        catch (Exception ex)
        {
            _fileLogger.LogException(ex, "Execution provider registration failed");
            throw;
        }

        _idleTimer = new System.Timers.Timer(60000); // check every minute
        _idleTimer.Elapsed += (_, _) => CheckIdle(idleTtlMinutes);
        _idleTimer.Start();
    }

    public async Task EnsureModelsLoadedAsync()
    {
        await _loadSemaphore.WaitAsync();
        try
        {
            if (_manager == null)
                throw new InvalidOperationException("Foundry manager not initialized. Call InitializeAsync first.");

            var catalog = await _manager.GetCatalogAsync();

            if (_whisperModel == null)
            {
                _whisperModel = await catalog.GetModelAsync("whisper-tiny")
                    ?? throw new Exception("whisper-tiny not found in catalog");
                if (!await _whisperModel.IsCachedAsync())
                {
                    ModelDownloadProgress?.Invoke("whisper-tiny", 0f);
                    await _whisperModel.DownloadAsync(pct => ModelDownloadProgress?.Invoke("whisper-tiny", pct));
                }
                await _whisperModel.LoadAsync();
                _fileLogger.Log("whisper-tiny loaded.");
            }

            if (_chatModel == null)
            {
                _chatModel = await catalog.GetModelAsync("phi-3.5-mini")
                    ?? throw new Exception("phi-3.5-mini not found in catalog");
                if (!await _chatModel.IsCachedAsync())
                {
                    ModelDownloadProgress?.Invoke("phi-3.5-mini", 0f);
                    await _chatModel.DownloadAsync(pct => ModelDownloadProgress?.Invoke("phi-3.5-mini", pct));
                }
                await _chatModel.LoadAsync();
                _fileLogger.Log("phi-3.5-mini loaded.");
            }

            _lastUsed = DateTime.Now;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public async Task<string> TranscribeAsync(string wavPath, string language, CancellationToken ct)
    {
        if (_whisperModel == null) throw new InvalidOperationException("Whisper not loaded");
        _lastUsed = DateTime.Now;

        var audioClient = await _whisperModel.GetAudioClientAsync();
        audioClient.Settings.Language = language;

        var sb = new System.Text.StringBuilder();
        var stream = audioClient.TranscribeAudioStreamingAsync(wavPath, ct);
        await foreach (var chunk in stream)
        {
            ct.ThrowIfCancellationRequested();
            sb.Append(chunk.Text);
        }

        _fileLogger.Log($"Transcription: {sb}");
        return sb.ToString();
    }

    public async Task<string> CleanupAsync(string rawText, Models.FormatMode mode, CancellationToken ct)
    {
        if (_chatModel == null) throw new InvalidOperationException("Chat model not loaded");
        _lastUsed = DateTime.Now;

        var chatClient = await _chatModel.GetChatClientAsync();
        var systemPrompt = GetSystemPrompt(mode);

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = rawText }
        };

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

        _fileLogger.Log($"Cleaned text: {result}");
        return result;
    }

    private static string GetSystemPrompt(Models.FormatMode mode)
    {
        return mode switch
        {
            Models.FormatMode.Standard => "Fix grammar, punctuation, and capitalization. Preserve the exact meaning. Do not expand abbreviations. Do not add salutations or sign-offs.",
            Models.FormatMode.Formal => "Fix grammar, punctuation, and capitalization. Remove filler words (um, like, you know). Expand common contractions. Tighten prose while preserving meaning. Do not add salutations or sign-offs.",
            Models.FormatMode.Raw => "Return the text exactly as provided, with no changes.",
            _ => "Fix grammar, punctuation, and capitalization."
        };
    }

    private void CheckIdle(int ttlMinutes)
    {
        if (_whisperModel == null && _chatModel == null) return;
        if ((DateTime.Now - _lastUsed).TotalMinutes < ttlMinutes) return;

        _ = Task.Run(async () =>
        {
            await _loadSemaphore.WaitAsync();
            try
            {
                // Re-check conditions inside the lock
                if (_whisperModel == null && _chatModel == null) return;
                if ((DateTime.Now - _lastUsed).TotalMinutes < ttlMinutes) return;

                _fileLogger.Log("Models idle — unloading.");
                if (_chatModel != null) { await _chatModel.UnloadAsync(); _chatModel = null; }
                if (_whisperModel != null) { await _whisperModel.UnloadAsync(); _whisperModel = null; }
            }
            finally
            {
                _loadSemaphore.Release();
            }
        });
    }

    public void Dispose()
    {
        _idleTimer?.Stop();
        _idleTimer?.Dispose();
        _loadSemaphore.Dispose();

        // Fire-and-forget on a background thread to avoid UI-thread deadlock
        _ = Task.Run(async () =>
        {
            try
            {
                if (_chatModel != null) await _chatModel.UnloadAsync();
                if (_whisperModel != null) await _whisperModel.UnloadAsync();
                _manager?.Dispose();
            }
            catch (Exception ex)
            {
                _fileLogger.LogException(ex, "FoundryOrchestrator dispose");
            }
        });
    }

    // Minimal ILogger provider wrapper
    private class FileLogProvider : ILoggerProvider
    {
        private readonly FileLogger _logger;
        public FileLogProvider(FileLogger logger) => _logger = logger;
        public ILogger CreateLogger(string categoryName) => new Wrapper(_logger, categoryName);
        public void Dispose() { }

        private class Wrapper : ILogger
        {
            private readonly FileLogger _logger;
            private readonly string _cat;
            public Wrapper(FileLogger logger, string cat) { _logger = logger; _cat = cat; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _logger.Log($"[{_cat}] {formatter(state, exception)}");
            }
        }
    }
}
