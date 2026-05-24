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
            _overlay = new RecordingOverlay();
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

    public void ShowPreviewToast(string text)
    {
        _dispatcher.Invoke(() =>
        {
            _preview?.Close();
            _preview = new PreviewToast(text, _clipboardService);
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
