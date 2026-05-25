using System.IO;
using System.Windows;
using System.Windows.Threading;
using Prompter.Models;

namespace Prompter.Services;

public class PipelineOrchestrator : IPipelineOrchestrator
{
    private readonly IAudioRecorderService _recorder;
    private readonly IModelManager _modelManager;
    private readonly ITranscriptionService _transcriptionService;
    private readonly ITextFormatter _textFormatter;
    private readonly IClipboardService _clipboard;
    private readonly IInputInjectorService _injector;
    private readonly IConfigService _configService;
    private readonly IAudioFeedbackService _audioFeedback;
    private readonly IFileLogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly IRecordingUIManager _uiManager;

    private IRecordingSession? _session;
    private CancellationTokenSource? _maxDurationCts;
    private DateTime _recordingStartTime;
    private readonly object _stopLock = new();
    private bool _isStopping;
    private int _recordingGeneration;

    public event Action<string>? OutputReady;
    public event Action<string, string>? ShowBalloon;

    public PipelineOrchestrator(
        IAudioRecorderService recorder,
        IModelManager modelManager,
        ITranscriptionService transcriptionService,
        ITextFormatter textFormatter,
        IClipboardService clipboard,
        IInputInjectorService injector,
        IConfigService configService,
        IAudioFeedbackService audioFeedback,
        IFileLogger logger,
        Dispatcher dispatcher,
        IRecordingUIManager uiManager)
    {
        _recorder = recorder;
        _modelManager = modelManager;
        _transcriptionService = transcriptionService;
        _textFormatter = textFormatter;
        _clipboard = clipboard;
        _injector = injector;
        _configService = configService;
        _audioFeedback = audioFeedback;
        _logger = logger;
        _dispatcher = dispatcher;
        _uiManager = uiManager;
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
                MessageBox.Show(
                    "Could not start recording. Make sure your microphone is not in use by another app (e.g., Teams, Zoom).",
                    "Prompter — Microphone Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
            return;
        }

        // Only show feedback once the microphone is actually capturing.
        _uiManager.ShowRecordingOverlay();

        // Fire-and-forget so the chime doesn't block the hook thread or the overlay.
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
            MessageBox.Show(
                "Recording was interrupted. The microphone may have been disconnected or taken by another app.",
                "Prompter — Recording Stopped",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
        if (!_modelManager.WhisperReady || !_modelManager.ChatReady)
        {
            _uiManager.UpdateProcessingStage("Loading models…");
        }
        await _modelManager.EnsureModelsLoadedAsync();

        var configuredAlias = string.IsNullOrWhiteSpace(cfg.ChatModelId)
            ? ModelCatalog.DefaultChatAlias
            : cfg.ChatModelId;
        if (_modelManager.ChatReady && _modelManager.LoadedChatModelAlias != configuredAlias)
        {
            var loadedAlias = _modelManager.LoadedChatModelAlias!;
            _logger.Log($"Chat model fallback active. Configured: {configuredAlias}, Loaded: {loadedAlias}.");
            ShowBalloon?.Invoke(
                "Prompter — Model fallback",
                $"Chat model '{configuredAlias}' was unavailable. Using '{loadedAlias}' instead.");
        }

        _uiManager.UpdateProcessingStage("Transcribing…");
        var rawText = await _transcriptionService.TranscribeAsync(wavPath, cfg.Language, cts.Token);
        rawText = rawText.Trim();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            _logger.Log("Transcription empty.");
            _uiManager.HideRecordingOverlay();
            return;
        }
        var trueRawText = rawText;
        if (cfg.SpokenPunctuationEnabled)
        {
            rawText = SpokenPunctuationProcessor.Process(rawText, cfg.Language, _logger);
        }
        _logger.Log($"Transcription complete using Whisper model '{_modelManager.LoadedWhisperModelAlias ?? "unknown"}'.");

        string finalText;
        bool usedFallback = false;

        if (mode?.ShowDiagnosticOutput == true)
        {
            string formattedText;
            string statusLine;

            if (_modelManager.ChatReady && mode?.SkipFormatting != true)
            {
                _uiManager.UpdateProcessingStage("Formatting…");
                try
                {
                    formattedText = await _textFormatter.CleanupAsync(rawText, modeId, cts.Token);
                    statusLine = "Chat model: " + (_modelManager.LoadedChatModelAlias ?? "unknown");
                }
                catch (Exception ex)
                {
                    _logger.LogException(ex, "CleanupAsync failed in diagnostic mode");
                    formattedText = rawText;
                    statusLine = "Chat model failed — raw text shown below";
                }
            }
            else
            {
                formattedText = rawText;
                statusLine = mode?.SkipFormatting == true
                    ? "Formatting skipped — raw text shown below"
                    : "Chat model not loaded — raw text shown below";
            }

            finalText = $"[RAW]{Environment.NewLine}{trueRawText}{Environment.NewLine}{Environment.NewLine}[FORMATTED]{Environment.NewLine}{formattedText}{Environment.NewLine}{Environment.NewLine}[STATUS]{Environment.NewLine}{statusLine}";
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
            _uiManager.UpdateProcessingStage("Formatting…");
            try
            {
                finalText = await _textFormatter.CleanupAsync(rawText, modeId, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "CleanupAsync failed — falling back to raw transcription");
                finalText = rawText;
                usedFallback = true;
            }
        }

        if (usedFallback)
        {
            ShowBalloon?.Invoke(
                "Prompter — Text formatted",
                "The formatting model was unavailable. Outputting raw transcription.");
        }

        _uiManager.UpdateProcessingStage("Typing…");

        var snapshot = _dispatcher.Invoke(() => _clipboard.SaveClipboard());

        try
        {
            if (cfg.UseClipboardPaste && finalText.Length >= cfg.PasteThresholdCharacters)
            {
                _dispatcher.Invoke(() => _clipboard.CopyText(finalText));
                _injector.SimulatePaste();
                await Task.Delay(150);
                _logger.Log("Text pasted via clipboard.");
            }
            else
            {
                _injector.TypeText(finalText);
                _logger.Log("Text injected.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Text injection/paste failed");
        }
        finally
        {
            _dispatcher.Invoke(() => _clipboard.RestoreClipboard(snapshot));
        }

        if (_recordingGeneration == generation)
        {
            _uiManager.HideRecordingOverlay();
            if (cfg.NotifyOnOutputReady)
            {
                _uiManager.ShowPreviewToast(finalText);
            }
        }
        OutputReady?.Invoke(finalText);
    }

    public void Dispose()
    {
        _maxDurationCts?.Cancel();
        _maxDurationCts?.Dispose();
        _session?.Dispose();
        _uiManager.Dispose();
    }
}
