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
        _audioFeedback.PlayStart();
        _recordingStartTime = DateTime.Now;

        try
        {
            _session = _recorder.StartRecording();
            _session.RecordingError += OnRecordingError;
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

        _uiManager.ShowRecordingOverlay();
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
            StopRecordingAndProcess(cfg.DefaultMode);
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

    public void StopRecordingAndProcess(FormatMode mode)
    {
        lock (_stopLock)
        {
            if (_isStopping) return;
            _isStopping = true;
        }

        _logger.Log("Pipeline: StopRecordingAndProcess called.");
        _audioFeedback.PlayStop();
        _maxDurationCts?.Cancel();
        _session?.StopRecording();

        _uiManager.HideRecordingOverlay();

        var elapsed = DateTime.Now - _recordingStartTime;
        if (elapsed < TimeSpan.FromSeconds(1))
        {
            _logger.Log($"Recording was very short ({elapsed.TotalMilliseconds:F0} ms).");
            ShowBalloon?.Invoke(
                "Prompter — Recording too short",
                "Hold the hotkey for at least a second while you speak.");
            _session?.Dispose();
            _session = null;
            return;
        }

        var wavPath = _session?.RecordedFilePath;
        if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
        {
            _logger.Log("No recording file found.");
            _session?.Dispose();
            _session = null;
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessAsync(wavPath, mode);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Processing pipeline");
                ShowBalloon?.Invoke(
                    "Prompter — Processing failed",
                    "Could not transcribe the recording. Check the logs for details.");
            }
            finally
            {
                try { File.Delete(wavPath); } catch { }
                _session?.Dispose();
                _session = null;
            }
        });
    }

    private async Task ProcessAsync(string wavPath, FormatMode mode)
    {
        var cfg = _configService.Load();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cfg.ProcessingTimeoutSeconds));

        _logger.Log("Ensuring models are loaded...");
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

        var rawText = await _transcriptionService.TranscribeAsync(wavPath, cfg.Language, cts.Token);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            _logger.Log("Transcription empty.");
            return;
        }

        string finalText;
        bool usedFallback = false;

        if (mode == FormatMode.Debug)
        {
            string formattedText;
            string statusLine;

            if (_modelManager.ChatReady)
            {
                try
                {
                    formattedText = await _textFormatter.CleanupAsync(rawText, FormatMode.Standard, cts.Token);
                    statusLine = "Chat model: " + (_modelManager.LoadedChatModelAlias ?? "unknown");
                }
                catch (Exception ex)
                {
                    _logger.LogException(ex, "CleanupAsync failed in Debug mode");
                    formattedText = rawText;
                    statusLine = "Chat model failed — raw text shown below";
                }
            }
            else
            {
                formattedText = rawText;
                statusLine = "Chat model not loaded — raw text shown below";
            }

            finalText = $"[RAW]{Environment.NewLine}{rawText}{Environment.NewLine}{Environment.NewLine}[FORMATTED]{Environment.NewLine}{formattedText}{Environment.NewLine}{Environment.NewLine}[STATUS]{Environment.NewLine}{statusLine}";
        }
        else if (mode == FormatMode.Raw || !_modelManager.ChatReady)
        {
            if (mode != FormatMode.Raw)
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
                finalText = await _textFormatter.CleanupAsync(rawText, mode, cts.Token);
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

        _uiManager.ShowPreviewToast(finalText);
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
