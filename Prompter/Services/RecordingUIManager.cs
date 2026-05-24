using System.Windows;
using System.Windows.Threading;
using Prompter.Views;

namespace Prompter.Services;

public class RecordingUIManager : IRecordingUIManager
{
    private readonly IConfigService _configService;
    private readonly IClipboardService _clipboardService;
    private readonly Dispatcher _dispatcher;
    private RecordingOverlay? _overlay;
    private PreviewToast? _preview;

    public RecordingUIManager(IConfigService configService, IClipboardService clipboardService, Dispatcher dispatcher)
    {
        _configService = configService;
        _clipboardService = clipboardService;
        _dispatcher = dispatcher;
    }

    public void ShowRecordingOverlay()
    {
        _dispatcher.Invoke(() =>
        {
            var config = _configService.Load();
            if (config.RecordingOverlay?.Enabled != true)
                return;

            _overlay = new RecordingOverlay(config.RecordingOverlay, config.OverlayStyle);
            _overlay.Show();
        });
    }

    public void HideRecordingOverlay()
    {
        _dispatcher.Invoke(() =>
        {
            _overlay?.Close();
            _overlay = null;
        });
    }

    public void UpdateAudioLevel(double normalizedLevel)
    {
        _dispatcher.Invoke(() =>
        {
            _overlay?.UpdateAudioLevel(normalizedLevel);
        });
    }

    public void ShowPreviewToast(string text)
    {
        _dispatcher.Invoke(() =>
        {
            var config = _configService.Load();
            if (config.PreviewToast?.Placement?.Enabled != true)
                return;

            _preview?.Close();
            _preview = new PreviewToast(text, _clipboardService, config.PreviewToast, config.OverlayStyle);
            _preview.Show();
        });
    }

    public void ShowBalloonIfEnabled(string title, string message)
    {
        // Balloon is handled by AppEventCoordinator via tray icon
        // This method is a no-op in the UI manager; balloons are cross-cutting.
    }

    public void Dispose()
    {
        _dispatcher.Invoke(() =>
        {
            _overlay?.Close();
            _overlay = null;
            _preview?.Close();
            _preview = null;
        });
    }
}
