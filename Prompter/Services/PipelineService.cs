using System.IO;
using System.Windows;
using System.Windows.Threading;
using Prompter.Models;
using Prompter.Views;

namespace Prompter.Services;

public class PipelineService : IDisposable
{
    private readonly AudioRecorderService _recorder;
    private readonly FoundryOrchestrator _foundry;
    private readonly ClipboardService _clipboard;
    private readonly InputInjectorService _injector;
    private readonly ConfigService _configService;
    private readonly AudioFeedbackService _audioFeedback;
    private readonly FileLogger _logger;
    private readonly Dispatcher _dispatcher;

    private RecordingOverlay? _overlay;
    private PreviewToast? _preview;
    private CancellationTokenSource? _maxDurationCts;

    public event Action<string>? OutputReady;
    public event Action<string, string>? ShowBalloon;

    private void ShowBalloonIfEnabled(string title, string message)
    {
        if (_configService.Load().NotificationsEnabled)
            ShowBalloon?.Invoke(title, message);
    }

    public PipelineService(
        AudioRecorderService recorder,
        FoundryOrchestrator foundry,
        ClipboardService clipboard,
        InputInjectorService injector,
        ConfigService configService,
        AudioFeedbackService audioFeedback,
        FileLogger logger,
        Dispatcher dispatcher)
    {
        _recorder = recorder;
        _foundry = foundry;
        _clipboard = clipboard;
        _injector = injector;
        _configService = configService;
        _audioFeedback = audioFeedback;
        _logger = logger;
        _dispatcher = dispatcher;

        _recorder.RecordingError += ex =>
        {
            _logger.LogException(ex, "RecordingError event");
            _dispatcher.Invoke(() =>
            {
                _overlay?.Close();
                _overlay = null;
                MessageBox.Show(
                    "Recording was interrupted. The microphone may have been disconnected or taken by another app.",
                    "Prompter — Recording Stopped",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        };
    }

    private DateTime _recordingStartTime;

    public void StartRecording()
    {
        _logger.Log("Pipeline: StartRecording called.");
        _audioFeedback.PlayStart();
        _recordingStartTime = DateTime.Now;

        if (!_recorder.StartRecording())
        {
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

        // Show overlay on UI thread
        _dispatcher.Invoke(() =>
        {
            _overlay = new RecordingOverlay();
            _overlay.Show();
        });

        ShowBalloonIfEnabled("Prompter — Recording", "Listening... Release the hotkey to stop.");

        // 5-minute safety cap
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

        // Kick off model loading in background
        _ = Task.Run(async () =>
        {
            try { await _foundry.EnsureModelsLoadedAsync(); }
            catch (Exception ex) { _logger.LogException(ex, "Background model load"); }
        });
    }

    public void StopRecordingAndProcess(FormatMode mode)
    {
        _logger.Log("Pipeline: StopRecordingAndProcess called.");
        _audioFeedback.PlayStop();
        _maxDurationCts?.Cancel();
        _recorder.StopRecording();

        _dispatcher.Invoke(() => _overlay?.Close());
        _overlay = null;

        var elapsed = DateTime.Now - _recordingStartTime;
        if (elapsed < TimeSpan.FromSeconds(1))
        {
            _logger.Log($"Recording was very short ({elapsed.TotalMilliseconds:F0} ms).");
            ShowBalloonIfEnabled(
                "Prompter — Recording too short",
                "Hold the hotkey for at least a second while you speak.");
            var shortPath = _recorder.RecordedFilePath;
            if (!string.IsNullOrEmpty(shortPath) && File.Exists(shortPath))
            {
                try { File.Delete(shortPath); } catch { }
            }
            return;
        }

        var wavPath = _recorder.RecordedFilePath;
        if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
        {
            _logger.Log("No recording file found.");
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
                ShowBalloonIfEnabled(
                    "Prompter — Processing failed",
                    "Could not transcribe the recording. Check the logs for details.");
            }
            finally
            {
                try { File.Delete(wavPath); } catch { }
            }
        });
    }

    private async Task ProcessAsync(string wavPath, FormatMode mode)
    {
        var cfg = _configService.Load();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        // Ensure models are loaded and up-to-date (handles model drift)
        _logger.Log("Ensuring models are loaded...");
        await _foundry.EnsureModelsLoadedAsync();

        // Notify if chat model fallback occurred
        var configuredAlias = string.IsNullOrWhiteSpace(cfg.ChatModelId)
            ? ModelCatalog.DefaultChatAlias
            : cfg.ChatModelId;
        if (_foundry.ChatReady && _foundry.LoadedChatModelAlias != configuredAlias)
        {
            var loadedAlias = _foundry.LoadedChatModelAlias!;
            var loadedDisplay = await _foundry.GetModelDisplayNameAsync(loadedAlias) ?? loadedAlias;
            _logger.Log($"Chat model fallback active. Configured: {configuredAlias}, Loaded: {_foundry.LoadedChatModelAlias}.");
            ShowBalloonIfEnabled(
                "Prompter — Model fallback",
                $"Chat model '{configuredAlias}' was unavailable. Using '{loadedDisplay}' instead.");
        }

        var rawText = await _foundry.TranscribeAsync(wavPath, cfg.Language, cts.Token);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            _logger.Log("Transcription empty.");
            return;
        }

        string finalText;
        bool usedFallback = false;

        if (mode == FormatMode.Debug)
        {
            // Debug mode: show both raw and formatted output
            string formattedText;
            string statusLine;

            if (_foundry.ChatReady)
            {
                try
                {
                    formattedText = await _foundry.CleanupAsync(rawText, FormatMode.Standard, cts.Token);
                    statusLine = "Chat model: " + (_foundry.LoadedChatModelAlias ?? "unknown");
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
        else if (mode == FormatMode.Raw || !_foundry.ChatReady)
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
                finalText = await _foundry.CleanupAsync(rawText, mode, cts.Token);
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
            ShowBalloonIfEnabled(
                "Prompter — Text formatted",
                "The formatting model was unavailable. Outputting raw transcription.");
        }

        // Clipboard guard
        var snapshot = _dispatcher.Invoke(() => _clipboard.SaveClipboard());

        try
        {
            // Inject
            if (cfg.UseClipboardPaste && finalText.Length >= cfg.PasteThresholdCharacters)
            {
                _dispatcher.Invoke(() => _clipboard.CopyText(finalText));
                _injector.SimulatePaste();
                // Wait for the target application to process the paste message before restoring original clipboard contents
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
            // Always restore clipboard
            _dispatcher.Invoke(() => _clipboard.RestoreClipboard(snapshot));
        }

        // Show preview toast
        _dispatcher.Invoke(() =>
        {
            _preview = new PreviewToast(finalText, _clipboard);
            _preview.Show();
        });

        OutputReady?.Invoke(finalText);
    }

    public void Dispose()
    {
        _maxDurationCts?.Cancel();
        _maxDurationCts?.Dispose();
    }
}
