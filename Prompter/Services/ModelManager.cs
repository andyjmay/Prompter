using System.Timers;
using Prompter.Models;

namespace Prompter.Services;

public class ModelManager : IModelManager, IAsyncDisposable
{
    private readonly IFoundryLocalManagerAccessor _accessor;
    private readonly IConfigService _configService;
    private readonly IFileLogger _fileLogger;
    private Microsoft.AI.Foundry.Local.IModel? _whisperModel;
    private Microsoft.AI.Foundry.Local.IModel? _chatModel;
    private bool _whisperLoaded;
    private bool _chatLoaded;
    private string? _loadedChatAlias;
    private string? _loadedWhisperAlias;
    private System.Timers.Timer? _idleTimer;
    private DateTime _lastUsed;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    private CancellationTokenSource? _idleCts;
    private Task? _idleTask;
    private bool _disposed;
    private ElapsedEventHandler? _idleTimerHandler;

    public bool WhisperReady => _whisperModel != null && _whisperLoaded;
    public bool ChatReady => _chatModel != null && _chatLoaded;
    public string? LoadedChatModelAlias => _loadedChatAlias;
    public string? LoadedWhisperModelAlias => _loadedWhisperAlias;

    public event Action<string, float>? ModelDownloadProgress;

    public ModelManager(IFoundryLocalManagerAccessor accessor, IConfigService configService, IFileLogger fileLogger)
    {
        _accessor = accessor;
        _configService = configService;
        _fileLogger = fileLogger;
    }

    public async Task InitializeAsync(int idleTtlMinutes)
    {
        await _accessor.InitializeAsync(idleTtlMinutes);

        _idleTimer?.Stop();
        if (_idleTimer != null && _idleTimerHandler != null)
        {
            _idleTimer.Elapsed -= _idleTimerHandler;
        }
        _idleTimer?.Dispose();
        _idleCts?.Cancel();
        _idleCts?.Dispose();

        _idleCts = new CancellationTokenSource();
        _idleTimer = new System.Timers.Timer(60000);
        _idleTimerHandler = (_, _) => _idleTask = Task.Run(() => CheckIdleAsync(idleTtlMinutes, _idleCts.Token));
        _idleTimer.Elapsed += _idleTimerHandler;
        _idleTimer.Start();
    }

    public async Task EnsureModelsLoadedAsync()
    {
        await _loadSemaphore.WaitAsync();
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ModelManager));

            var catalog = await _accessor.Manager.GetCatalogAsync();
            var cfg = _configService.Load();
            var targetChatAlias = string.IsNullOrWhiteSpace(cfg.ChatModelId)
                ? ModelCatalog.DefaultChatAlias
                : cfg.ChatModelId;

            var targetWhisperAlias = string.IsNullOrWhiteSpace(cfg.WhisperModelId)
                ? "whisper-tiny"
                : cfg.WhisperModelId;

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

    private async Task<Microsoft.AI.Foundry.Local.IModel?> TryLoadChatModelAsync(Microsoft.AI.Foundry.Local.ICatalog catalog, string alias)
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
                var catalog = await _accessor.Manager.GetCatalogAsync();
                var model = await catalog.GetModelAsync(alias);
                if (model != null)
                {
                    await model.UnloadAsync();
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
        var catalog = await _accessor.Manager.GetCatalogAsync();
        var model = await catalog.GetModelAsync(alias);
        if (model != null)
        {
            ModelDownloadProgress?.Invoke(alias, 0f);
            await model.DownloadAsync(pct => ModelDownloadProgress?.Invoke(alias, pct));
            ModelDownloadProgress?.Invoke(alias, 100f);
        }
    }

    private async Task CheckIdleAsync(int ttlMinutes, CancellationToken ct)
    {
        if (_whisperModel == null && _chatModel == null) return;
        if ((DateTime.Now - _lastUsed).TotalMinutes < ttlMinutes) return;
        if (ct.IsCancellationRequested) return;

        await _loadSemaphore.WaitAsync(ct);
        try
        {
            if (_whisperModel == null && _chatModel == null) return;
            if ((DateTime.Now - _lastUsed).TotalMinutes < ttlMinutes) return;
            if (ct.IsCancellationRequested) return;

            _fileLogger.Log("Models idle — unloading.");
            if (_chatModel != null) { await _chatModel.UnloadAsync(); _chatModel = null; _chatLoaded = false; _loadedChatAlias = null; }
            if (_whisperModel != null) { await _whisperModel.UnloadAsync(); _whisperModel = null; _whisperLoaded = false; _loadedWhisperAlias = null; }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _idleTimer?.Stop();
        _idleCts?.Cancel();
        if (_idleTask != null)
        {
            try { await _idleTask; } catch (OperationCanceledException) { }
        }
        _idleTimer?.Dispose();
        _idleCts?.Dispose();

        await _loadSemaphore.WaitAsync();
        try
        {
            if (_chatModel != null) { await _chatModel.UnloadAsync(); _chatLoaded = false; _loadedChatAlias = null; }
            if (_whisperModel != null) { await _whisperModel.UnloadAsync(); _whisperLoaded = false; _loadedWhisperAlias = null; }
        }
        catch (Exception ex)
        {
            _fileLogger.LogException(ex, "ModelManager dispose");
        }
        finally
        {
            _loadSemaphore.Release();
            _loadSemaphore.Dispose();
        }
    }
}
