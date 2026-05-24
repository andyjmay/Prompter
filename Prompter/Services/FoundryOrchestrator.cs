using System.Threading;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Prompter.Models;

namespace Prompter.Services;

public class FoundryOrchestrator : IDisposable
{
    private readonly FileLogger _fileLogger;
    private readonly ConfigService _configService;
    private FoundryLocalManager? _manager;
    private Microsoft.AI.Foundry.Local.IModel? _whisperModel;
    private Microsoft.AI.Foundry.Local.IModel? _chatModel;
    private bool _whisperLoaded;
    private bool _chatLoaded;
    private string? _loadedChatAlias;
    private string? _loadedWhisperAlias;
    private System.Timers.Timer? _idleTimer;
    private DateTime _lastUsed;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);

    public bool WhisperReady => _whisperModel != null && _whisperLoaded;
    public bool ChatReady => _chatModel != null && _chatLoaded;
    public string? LoadedChatModelAlias => _loadedChatAlias;
    public string? LoadedWhisperModelAlias => _loadedWhisperAlias;
    public bool IsInitialized => _manager != null;

    public event Action<string, float>? ModelDownloadProgress;

    public FoundryOrchestrator(FileLogger fileLogger, ConfigService configService)
    {
        _fileLogger = fileLogger;
        _configService = configService;
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
            var cfg = _configService.Load();
            var targetChatAlias = string.IsNullOrWhiteSpace(cfg.ChatModelId)
                ? ModelCatalog.DefaultChatAlias
                : cfg.ChatModelId;

            var targetWhisperAlias = string.IsNullOrWhiteSpace(cfg.WhisperModelId)
                ? "whisper-tiny"
                : cfg.WhisperModelId;

            // Whisper: detect model drift or first load
            bool needsWhisperReload = _whisperModel == null;
            if (_whisperModel != null && _loadedWhisperAlias != targetWhisperAlias)
            {
                _fileLogger.Log($"Whisper model changed from '{_loadedWhisperAlias}' to '{targetWhisperAlias}'. Unloading old model.");
                await _whisperModel.UnloadAsync();
                _whisperModel = null;
                _whisperLoaded = false;
                _loadedWhisperAlias = null;
                needsWhisperReload = true;
            }

            if (needsWhisperReload)
            {
                _whisperModel = await catalog.GetModelAsync(targetWhisperAlias);
                if (_whisperModel == null)
                {
                    _fileLogger.Log($"Configured Whisper model '{targetWhisperAlias}' unavailable. Falling back to default 'whisper-tiny'.");
                    _whisperModel = await catalog.GetModelAsync("whisper-tiny")
                        ?? throw new Exception("whisper-tiny not found in catalog");
                    targetWhisperAlias = "whisper-tiny";
                }
                if (!await _whisperModel.IsCachedAsync())
                {
                    ModelDownloadProgress?.Invoke(targetWhisperAlias, 0f);
                    await _whisperModel.DownloadAsync(pct => ModelDownloadProgress?.Invoke(targetWhisperAlias, pct));
                }
                await _whisperModel.LoadAsync();
                _whisperLoaded = true;
                _loadedWhisperAlias = targetWhisperAlias;
                _fileLogger.Log($"{targetWhisperAlias} loaded.");
            }

            // Chat: detect model drift or first load
            bool needsReload = _chatModel == null;
            if (_chatModel != null && _loadedChatAlias != targetChatAlias)
            {
                _fileLogger.Log($"Chat model changed from '{_loadedChatAlias}' to '{targetChatAlias}'. Unloading old model.");
                await _chatModel.UnloadAsync();
                _chatModel = null;
                _chatLoaded = false;
                _loadedChatAlias = null;
                needsReload = true;
            }

            if (needsReload)
            {
                _chatModel = await TryLoadChatModelAsync(catalog, targetChatAlias);
                if (_chatModel == null)
                {
                    _fileLogger.Log($"Configured chat model '{targetChatAlias}' unavailable. Falling back to default '{ModelCatalog.DefaultChatAlias}'.");
                    _chatModel = await TryLoadChatModelAsync(catalog, ModelCatalog.DefaultChatAlias);
                    if (_chatModel == null)
                        throw new InvalidOperationException($"Could not load chat model '{targetChatAlias}' or default '{ModelCatalog.DefaultChatAlias}'.");
                }
                _loadedChatAlias = targetChatAlias;
                _chatLoaded = true;
            }

            _lastUsed = DateTime.Now;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    private async Task<Microsoft.AI.Foundry.Local.IModel?> TryLoadChatModelAsync(ICatalog catalog, string alias)
    {
        try
        {
            var model = await catalog.GetModelAsync(alias);
            if (model == null)
            {
                _fileLogger.Log($"Model alias '{alias}' not found in catalog.");
                return null;
            }
            if (!await model.IsCachedAsync())
            {
                ModelDownloadProgress?.Invoke(alias, 0f);
                await model.DownloadAsync(pct => ModelDownloadProgress?.Invoke(alias, pct));
            }
            await model.LoadAsync();
            _fileLogger.Log($"{alias} loaded.");
            return model;
        }
        catch (Exception ex)
        {
            _fileLogger.LogException(ex, $"Failed to load chat model {alias}");
            return null;
        }
    }

    public async Task UnloadChatModelAsync()
    {
        await _loadSemaphore.WaitAsync();
        try
        {
            if (_chatModel != null)
            {
                await _chatModel.UnloadAsync();
                _chatModel = null;
                _chatLoaded = false;
                _loadedChatAlias = null;
                _fileLogger.Log("Chat model unloaded (settings change).");
            }
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public async Task UnloadWhisperModelAsync()
    {
        await _loadSemaphore.WaitAsync();
        try
        {
            if (_whisperModel != null)
            {
                await _whisperModel.UnloadAsync();
                _whisperModel = null;
                _whisperLoaded = false;
                _loadedWhisperAlias = null;
                _fileLogger.Log("Whisper model unloaded.");
            }
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public async Task UnloadModelAsync(string alias)
    {
        await _loadSemaphore.WaitAsync();
        try
        {
            if (_loadedChatAlias == alias && _chatModel != null)
            {
                await _chatModel.UnloadAsync();
                _chatModel = null;
                _chatLoaded = false;
                _loadedChatAlias = null;
                _fileLogger.Log($"Model {alias} (chat) unloaded.");
            }
            else if (_loadedWhisperAlias == alias && _whisperModel != null)
            {
                await _whisperModel.UnloadAsync();
                _whisperModel = null;
                _whisperLoaded = false;
                _loadedWhisperAlias = null;
                _fileLogger.Log($"Model {alias} (whisper) unloaded.");
            }
            else
            {
                if (_manager != null)
                {
                    var catalog = await _manager.GetCatalogAsync();
                    var model = await catalog.GetModelAsync(alias);
                    if (model != null)
                    {
                        await model.UnloadAsync();
                    }
                }
            }
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public async Task DownloadModelAsync(string alias)
    {
        if (_manager == null) return;
        var catalog = await _manager.GetCatalogAsync();
        var model = await catalog.GetModelAsync(alias);
        if (model != null)
        {
            ModelDownloadProgress?.Invoke(alias, 0f);
            await model.DownloadAsync(pct => ModelDownloadProgress?.Invoke(alias, pct));
            ModelDownloadProgress?.Invoke(alias, 100f);
        }
    }

    public async Task<List<(string Alias, string DisplayName)>> ListAvailableWhisperModelsAsync(CancellationToken ct = default)
    {
        if (_manager == null)
            throw new InvalidOperationException("Foundry manager not initialized. Call InitializeAsync first.");

        var catalog = await _manager.GetCatalogAsync();
        var models = await catalog.ListModelsAsync(ct);
        var result = new List<(string Alias, string DisplayName)>();

        foreach (var model in models)
        {
            var alias = model.Alias;
            if (alias.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase))
            {
                var displayName = !string.IsNullOrWhiteSpace(model.Info?.DisplayName)
                    ? model.Info.DisplayName
                    : alias;
                result.Add((alias, displayName));
            }
        }

        return result.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<ModelStatusInfo>> GetModelStatusListAsync(CancellationToken ct = default)
    {
        if (_manager == null) return new List<ModelStatusInfo>();

        var catalog = await _manager.GetCatalogAsync();
        var models = await catalog.ListModelsAsync(ct);
        var result = new List<ModelStatusInfo>();

        foreach (var m in models)
        {
            var alias = m.Alias;
            var info = m.Info;

            string task = info?.Task ?? "Unknown";
            bool isWhisper = alias.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase);
            bool isChat = !isWhisper && 
                          (string.IsNullOrEmpty(task) || 
                           (!task.Contains("embed", StringComparison.OrdinalIgnoreCase) && 
                            !task.Contains("speech", StringComparison.OrdinalIgnoreCase) && 
                            !task.Contains("audio", StringComparison.OrdinalIgnoreCase)));

            if (!isWhisper && !isChat) continue;

            bool isCached = false;
            try { isCached = await m.IsCachedAsync(); } catch { }

            bool isLoaded = false;
            if (isWhisper && _whisperLoaded && _loadedWhisperAlias == alias) isLoaded = true;
            if (isChat && _chatLoaded && _loadedChatAlias == alias) isLoaded = true;

            string size = "Unknown";
            if (alias.Contains("tiny")) size = "~75 MB";
            else if (alias.Contains("base")) size = "~145 MB";
            else if (alias.Contains("small")) size = "~460 MB";
            else if (alias.Contains("phi-3.5")) size = "~2.2 GB";
            else if (alias.Contains("qwen")) size = "~0.5 GB";

            result.Add(new ModelStatusInfo
            {
                Alias = alias,
                DisplayName = info?.DisplayName ?? alias,
                IsCached = isCached,
                IsLoaded = isLoaded,
                SizeDescription = size,
                TaskType = isWhisper ? "Speech Transcription" : "Text Correction"
            });
        }

        return result.OrderBy(r => r.TaskType).ThenBy(r => r.DisplayName).ToList();
    }

    public async Task<bool> IsModelInCatalogAsync(string alias)
    {
        if (_manager == null) return false;
        try
        {
            var catalog = await _manager.GetCatalogAsync();
            var model = await catalog.GetModelAsync(alias);
            return model != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<(string Alias, string DisplayName)>> ListAvailableChatModelsAsync(CancellationToken ct = default)
    {
        if (_manager == null)
            throw new InvalidOperationException("Foundry manager not initialized. Call InitializeAsync first.");

        var catalog = await _manager.GetCatalogAsync();
        var models = await catalog.ListModelsAsync(ct);
        var result = new List<(string Alias, string DisplayName)>();

        foreach (var model in models)
        {
            var alias = model.Alias;
            var info = model.Info;

            // Exclude audio transcription models (whisper is hardcoded separately)
            if (alias.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase))
                continue;

            // Exclude embedding models by task type
            var task = info.Task;
            if (!string.IsNullOrEmpty(task))
            {
                if (task.Contains("embed", StringComparison.OrdinalIgnoreCase) ||
                    task.Contains("speech", StringComparison.OrdinalIgnoreCase) ||
                    task.Contains("audio", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var displayName = !string.IsNullOrWhiteSpace(info.DisplayName)
                ? info.DisplayName
                : alias;

            result.Add((alias, displayName));
        }

        return result.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string?> GetModelDisplayNameAsync(string alias, CancellationToken ct = default)
    {
        if (_manager == null) return null;
        try
        {
            var catalog = await _manager.GetCatalogAsync();
            var model = await catalog.GetModelAsync(alias, ct);
            return model?.Info?.DisplayName;
        }
        catch
        {
            return null;
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

    private string GetSystemPrompt(Models.FormatMode mode)
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

        // If output is way longer than input, it's probably a conversational response
        if (resultWords.Length > rawWords.Length * 2 && rawWords.Length > 0)
            return rawText;

        // If output shares zero words with input, it's almost certainly hallucinated
        var rawSet = new HashSet<string>(rawWords.Select(w => w.Trim(',', '.', '!', '?', ';', ':').ToLowerInvariant()));
        var overlap = resultWords.Count(w => rawSet.Contains(w.Trim(',', '.', '!', '?', ';', ':').ToLowerInvariant()));
        if (overlap == 0 && rawWords.Length > 0)
            return rawText;

        // If too few original words are preserved, the model probably rewrote everything
        if (rawWords.Length > 0)
        {
            var preservationRatio = (double)overlap / rawWords.Length;
            if (preservationRatio < 0.4)
                return rawText;
        }

        // If the output looks like an explanation (numbered lists, bullet points, headers), reject it
        var explanatoryPatterns = new[] { "1.", "2.", "3.", "4.", "5.", "* ", "- ", "**", "##", "###" };
        if (explanatoryPatterns.Any(p => result.Contains(p, StringComparison.Ordinal)))
            return rawText;

        return result;
    }

    private static string StripOutputWrappers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Strip prompt leakage markers that small models sometimes echo
        text = text.Replace("[DICTATED_TEXT_START]", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("[DICTATED_TEXT_END]", "", StringComparison.OrdinalIgnoreCase)
                   .Trim();

        var lines = text.Split('\n');
        var first = lines[0].Trim();

        // Strip common prefixes that small models love to add
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

        // Strip surrounding quotes
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
                if (_chatModel != null) { await _chatModel.UnloadAsync(); _chatModel = null; _chatLoaded = false; _loadedChatAlias = null; }
                if (_whisperModel != null) { await _whisperModel.UnloadAsync(); _whisperModel = null; _whisperLoaded = false; }
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
                if (_chatModel != null) { await _chatModel.UnloadAsync(); _chatLoaded = false; _loadedChatAlias = null; }
                if (_whisperModel != null) { await _whisperModel.UnloadAsync(); _whisperLoaded = false; }
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
