using System.IO;
using System.Windows.Threading;
using Prompter.Models;

namespace Prompter.Services;

public class PipelineOrchestrator : IPipelineOrchestrator
{
    private readonly IAudioRecorderService _recorder;
    private readonly IModelManager _modelManager;
    private readonly IPipelineProcessor _pipelineProcessor;
    private readonly IClipboardService _clipboard;
    private readonly IInputInjectorService _injector;
    private readonly IConfigService _configService;
    private readonly IAudioFeedbackService _audioFeedback;
    private readonly IFileLogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly IRecordingUIManager _uiManager;
    private readonly IDialogService _dialogService;

    private IRecordingSession? _session;
    private CancellationTokenSource? _maxDurationCts;
    private DateTime _recordingStartTime;
    private readonly object _stopLock = new();
    private bool _isStopping;
    private int _recordingGeneration;
    private readonly List<Task> _pendingClipboardRestores = new();
    private readonly object _pendingLock = new();

    public event Action<string>? OutputReady;
    public event Action<string, string>? ShowBalloon;

    public PipelineOrchestrator(
        IAudioRecorderService recorder,
        IModelManager modelManager,
        IPipelineProcessor pipelineProcessor,
        IClipboardService clipboard,
        IInputInjectorService injector,
        IConfigService configService,
        IAudioFeedbackService audioFeedback,
        IFileLogger logger,
        Dispatcher dispatcher,
        IRecordingUIManager uiManager,
        IDialogService dialogService)
    {
        _recorder = recorder;
        _modelManager = modelManager;
        _pipelineProcessor = pipelineProcessor;
        _clipboard = clipboard;
        _injector = injector;
        _configService = configService;
        _audioFeedback = audioFeedback;
        _logger = logger;
        _dispatcher = dispatcher;
        _uiManager = uiManager;
        _dialogService = dialogService;
    }

    public void StartRecording()
    {
        _logger.Log("Pipeline: StartRecording called.");
        _isStopping = false;
        _recordingGeneration++;

        _recordingStartTime = DateTime.Now;

        try
        {
            _session = _recorder.StartRecording();
            _session.RecordingError += OnRecordingError;
            _session.AudioLevelAvailable += OnAudioLevelAvailable;
            _session.Begin();
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "StartRecording failed");
            _dispatcher.Invoke(() =>
            {
                _dialogService.ShowWarning(
                    "Prompter — Microphone Error",
                    "Could not start recording. Make sure your microphone is not in use by another app (e.g., Teams, Zoom).");
            });
            return;
        }

        _uiManager.ShowRecordingOverlay();
        _ = Task.Run(() => _audioFeedback.PlayStart());
        ShowBalloon?.Invoke("Prompter — Recording", "Listening... Release the hotkey to stop.");

        _maxDurationCts?.Dispose();
        _maxDurationCts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), _maxDurationCts.Token);
            }
            catch (TaskCanceledException) { return; }
            _logger.Log("Pipeline: Max 5-minute duration reached — auto-stopping.");
            var cfg = _configService.Load();
            StopRecordingAndProcess(cfg.DefaultModeId);
        });

        _ = Task.Run(async () =>
        {
            try { await _modelManager.EnsureModelsLoadedAsync(); }
            catch (Exception ex) { _logger.LogException(ex, "Background model load"); }
        });
    }

    private void OnRecordingError(Exception ex)
    {
        _logger.LogException(ex, "RecordingError event");
        _dispatcher.Invoke(() =>
        {
            _uiManager.HideRecordingOverlay();
            _dialogService.ShowWarning(
                "Prompter — Recording Stopped",
                "Recording was interrupted. The microphone may have been disconnected or taken by another app.");
        });
    }

    private void OnAudioLevelAvailable(double level)
    {
        _uiManager.UpdateAudioLevel(level);
    }

    public void StopRecordingAndProcess(string modeId)
    {
        lock (_stopLock)
        {
            if (_isStopping) return;
            _isStopping = true;
        }

        var cfg = _configService.Load();
        var mode = cfg.Modes.FirstOrDefault(m => m.Id.Equals(modeId, StringComparison.OrdinalIgnoreCase));
        var modeName = mode?.Name ?? modeId;
        if (mode == null)
        {
            _logger.Log($"WARNING: Mode '{modeId}' not found in config. Falling back to default behavior.");
        }
        _logger.Log($"Pipeline: StopRecordingAndProcess called with mode '{modeName}' (id: {modeId}).");
        _audioFeedback.PlayStop();
        _maxDurationCts?.Cancel();
        _session?.StopRecording();
        var session = _session;
        var generation = _recordingGeneration;

        var elapsed = DateTime.Now - _recordingStartTime;
        if (elapsed < TimeSpan.FromSeconds(1))
        {
            _uiManager.HideRecordingOverlay();
            _logger.Log($"Recording was very short ({elapsed.TotalMilliseconds:F0} ms).");
            ShowBalloon?.Invoke(
                "Prompter — Recording too short",
                "Hold the hotkey for at least a second while you speak.");
            session?.Dispose();
            return;
        }

        _uiManager.TransitionOverlayToProcessing();

        var wavPath = session?.RecordedFilePath;
        if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
        {
            _logger.Log("No recording file found.");
            _uiManager.HideRecordingOverlay();
            session?.Dispose();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessAsync(wavPath, modeId, generation);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Processing pipeline");
                if (_recordingGeneration == generation)
                {
                    _uiManager.HideRecordingOverlay();
                }
                ShowBalloon?.Invoke(
                    "Prompter — Processing failed",
                    "Could not transcribe the recording. Check the logs for details.");
            }
            finally
            {
                try { File.Delete(wavPath); } catch { }
                session?.Dispose();
            }
        });
    }

    private async Task ProcessAsync(string wavPath, string modeId, int generation)
    {
        var cfg = _configService.Load();
        var mode = cfg.Modes.FirstOrDefault(m => m.Id.Equals(modeId, StringComparison.OrdinalIgnoreCase));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cfg.ProcessingTimeoutSeconds));

        _logger.Log("Ensuring models are loaded...");
        bool isNoneChat = string.Equals(cfg.ChatModelId, "none", StringComparison.OrdinalIgnoreCase);
        if (!_modelManager.WhisperReady || (!_modelManager.ChatReady && !isNoneChat))
        {
            _uiManager.UpdateProcessingStage("Loading models…");
        }

        var configuredAlias = string.IsNullOrWhiteSpace(cfg.ChatModelId)
            ? ModelCatalog.DefaultChatAlias
            : cfg.ChatModelId;

        var result = await _pipelineProcessor.ProcessAsync(wavPath, modeId, cts.Token, new Progress<string>(stage => _uiManager.UpdateProcessingStage(stage)));

        if (string.IsNullOrWhiteSpace(result.RawText))
        {
            _logger.Log("Transcription empty.");
            _uiManager.HideRecordingOverlay();
            return;
        }

        if (result.MatchedSnippetTrigger != null)
        {
            _logger.Log($"Snippet matched: '{result.MatchedSnippetTrigger}' — injecting expansion.");
            ShowBalloon?.Invoke("Prompter — Snippet inserted", $"Matched '{result.MatchedSnippetTrigger}'.");
            bool useSendKeys = false;
            if (_injector.ContainsKeyTokens(result.FinalText))
            {
                try
                {
                    _injector.ValidateExpansion(result.FinalText);
                    useSendKeys = true;
                }
                catch (ArgumentException ex)
                {
                    _logger.Log($"Snippet expansion contains invalid key tokens: {ex.Message}");
                }
            }
            await InjectAsync(result.FinalText, generation, cfg, useSendKeys);
            return;
        }

        if (_modelManager.ChatReady && _modelManager.LoadedChatModelAlias != configuredAlias)
        {
            var loadedAlias = _modelManager.LoadedChatModelAlias!;
            _logger.Log($"Chat model fallback active. Configured: {configuredAlias}, Loaded: {loadedAlias}.");
            ShowBalloon?.Invoke(
                "Prompter — Model fallback",
                $"Chat model '{configuredAlias}' was unavailable. Using '{loadedAlias}' instead.");
        }

        bool usedFallback = result.UsedFormattingFallback;
        string finalText = result.FinalText;

        if (usedFallback)
        {
            ShowBalloon?.Invoke(
                "Prompter — Text formatted",
                "The formatting model was unavailable. Outputting raw transcription.");
        }

        await InjectAsync(finalText, generation, cfg);
    }

    private async Task InjectAsync(string text, int generation, AppConfig cfg)
        => await InjectAsync(text, generation, cfg, useSendKeys: false);

    private async Task InjectAsync(string text, int generation, AppConfig cfg, bool useSendKeys)
    {
        _uiManager.UpdateProcessingStage(useSendKeys ? "Sending keys…" : "Typing…");

        try
        {
            if (useSendKeys)
            {
                _injector.SendKeys(text);
                _logger.Log("Keys injected via SendKeys.");
            }
            else if (cfg.UseClipboardPaste && text.Length >= cfg.PasteThresholdCharacters)
            {
                var snapshot = _dispatcher.Invoke(() => _clipboard.SaveClipboard());
                try
                {
                    _dispatcher.Invoke(() => _clipboard.CopyText(text));
                    _injector.SimulatePaste();
                    _logger.Log("Text pasted via clipboard.");

                    var delayMs = Math.Min(300 + text.Length * 10, 5000);
                    var restoreTask = Task.Run(async () =>
                    {
                        await Task.Delay(delayMs);
                        _dispatcher.Invoke(() => _clipboard.RestoreClipboard(snapshot));
                        _logger.Log($"Clipboard restored after {delayMs} ms.");
                    });
                    lock (_pendingLock)
                    {
                        _pendingClipboardRestores.Add(restoreTask);
                    }
                    _ = restoreTask.ContinueWith(_ =>
                    {
                        lock (_pendingLock)
                        {
                            _pendingClipboardRestores.Remove(restoreTask);
                        }
                    }, TaskScheduler.Default);
                }
                catch (Exception ex)
                {
                    _logger.LogException(ex, "Clipboard paste failed");
                    _dispatcher.Invoke(() => _clipboard.RestoreClipboard(snapshot));
                }
            }
            else
            {
                _injector.TypeText(text);
                _logger.Log("Text injected.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Text injection failed");
        }

        if (_recordingGeneration == generation)
        {
            _uiManager.HideRecordingOverlay();
            if (cfg.NotifyOnOutputReady)
            {
                _uiManager.ShowPreviewToast(text);
            }
        }
        OutputReady?.Invoke(text);
    }

    public void Dispose()
    {
        _maxDurationCts?.Cancel();
        _maxDurationCts?.Dispose();
        _session?.Dispose();
        _uiManager.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        Task[] pending;
        lock (_pendingLock)
        {
            pending = _pendingClipboardRestores.ToArray();
        }
        if (pending.Length > 0)
        {
            _logger.Log($"Waiting for {pending.Length} pending clipboard restore(s)...");
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
