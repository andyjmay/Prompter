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

    public void StartRecording()
    {
        _logger.Log("Pipeline: StartRecording called.");
        _audioFeedback.PlayStart();

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

        // Ensure whisper is ready
        if (!_foundry.WhisperReady)
        {
            _logger.Log("Waiting for whisper to load...");
            await _foundry.EnsureModelsLoadedAsync();
        }

        var rawText = await _foundry.TranscribeAsync(wavPath, cfg.Language, cts.Token);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            _logger.Log("Transcription empty.");
            return;
        }

        string finalText;
        bool usedFallback = false;
        if (mode == FormatMode.Raw || !_foundry.ChatReady)
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
            ShowBalloon?.Invoke(
                "Prompter — Text formatted",
                "The formatting model was unavailable. Outputting raw transcription.");
        }

        // Clipboard guard
        var snapshot = _dispatcher.Invoke(() => _clipboard.SaveClipboard());

        try
        {
            // Inject
            _injector.TypeText(finalText);
            _logger.Log("Text injected.");
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Text injection failed");
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
