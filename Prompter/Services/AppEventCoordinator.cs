using System.Windows.Threading;
using Prompter.ViewModels;
using Prompter.Views;

namespace Prompter.Services;

public class AppEventCoordinator : IDisposable
{
    private readonly IPipelineOrchestrator _pipeline;
    private readonly TrayIconViewModel _trayVm;
    private readonly TrayIconView _trayView;
    private readonly IConfigService _configService;
    private readonly IHotkeyService _hotkeyService;
    private readonly Action<string> _onOutputReady;
    private readonly Action<string, string> _onShowBalloon;
    private readonly Action _onRecordingStarted;
    private readonly Action _onRecordingStopped;

    public AppEventCoordinator(
        IPipelineOrchestrator pipeline,
        TrayIconViewModel trayVm,
        TrayIconView trayView,
        IConfigService configService,
        IHotkeyService hotkeyService)
    {
        _pipeline = pipeline;
        _trayVm = trayVm;
        _trayView = trayView;
        _configService = configService;
        _hotkeyService = hotkeyService;
        _onOutputReady = text => _trayVm.SetLastOutput(text);
        _onShowBalloon = OnShowBalloon;
        _onRecordingStarted = OnRecordingStarted;
        _onRecordingStopped = OnRecordingStopped;
    }

    public void Initialize()
    {
        _pipeline.OutputReady += _onOutputReady;
        _pipeline.ShowBalloon += _onShowBalloon;
        _hotkeyService.RecordingStarted += _onRecordingStarted;
        _hotkeyService.RecordingStopped += _onRecordingStopped;
    }

    public void Dispose()
    {
        _pipeline.OutputReady -= _onOutputReady;
        _pipeline.ShowBalloon -= _onShowBalloon;
        _hotkeyService.RecordingStarted -= _onRecordingStarted;
        _hotkeyService.RecordingStopped -= _onRecordingStopped;
    }

    private void OnShowBalloon(string title, string message)
    {
        var config = _configService.Load();
        if (!config.NotificationsEnabled) return;

        _trayView.Dispatcher.Invoke(() =>
        {
            _trayView.TrayIcon.ShowBalloonTip(title, message, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        });
    }

    private void OnRecordingStarted()
    {
        _pipeline.StartRecording();
    }

    private void OnRecordingStopped()
    {
        var config = _configService.Load();
        _pipeline.StopRecordingAndProcess(config.DefaultModeId);
    }
}
