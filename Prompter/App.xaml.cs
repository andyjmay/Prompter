using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Prompter.Services;
using Prompter.ViewModels;
using Prompter.Views;

namespace Prompter;

public partial class App : Application
{
    private TrayIconView? _trayView;
    private TrayIconViewModel? _trayVm;
    private Mutex? _singleInstanceMutex;

    // Keep references for disposal
    private AudioRecorderService? _recorder;
    private FoundryOrchestrator? _foundry;
    private PipelineService? _pipeline;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers — last line of defense
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        var logger = new FileLogger();
        logger.Log("Prompter starting.");

        // Single-instance enforcement
        const string mutexName = "Prompter_SingleInstance_Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            logger.Log("Another instance of Prompter is already running. Exiting.");
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        var configService = new ConfigService();
        var clipboardService = new ClipboardService(logger);
        _recorder = new AudioRecorderService(logger);
        var injector = new InputInjectorService(logger);
        var startupService = new StartupService();
        var audioFeedback = new AudioFeedbackService(configService);

        // Check first-run BEFORE Load() creates the config file
        bool isFirstRun = configService.IsFirstRun();

        // Sync registry with config on startup
        var config = configService.Load();
        if (config.AutoStartWithWindows != startupService.IsEnabled())
        {
            startupService.SetEnabled(config.AutoStartWithWindows);
        }

        _foundry = new FoundryOrchestrator(logger);

        // On first run, report model download progress via tray balloon
        if (isFirstRun)
        {
            _foundry.ModelDownloadProgress += (model, pct) =>
            {
                _trayView?.Dispatcher.Invoke(() =>
                {
                    _trayView?.TrayIcon.ShowBalloonTip(
                        "Prompter — Downloading AI models",
                        $"{model}: {pct:F0}%",
                        Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                });
            };
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var cfg = configService.Load();
                await _foundry.InitializeAsync(cfg.ModelIdleTtlMinutes);
                logger.Log("Foundry Local initialized.");
            }
            catch (Exception ex)
            {
                logger.LogException(ex, "Foundry Local init");
            }
        });

        _pipeline = new PipelineService(_recorder, _foundry, clipboardService, injector, configService, audioFeedback, logger, Dispatcher);

        var hotkey = new HotkeyService(logger);

        _trayVm = new TrayIconViewModel(configService, hotkey, _pipeline, clipboardService, startupService, logger);
        _pipeline.OutputReady += text => _trayVm.SetLastOutput(text);

        _trayView = new TrayIconView();
        _trayView.Loaded += (_, _) =>
        {
            _trayView.TrayIcon.DataContext = _trayVm;
            _trayVm.Initialize(_trayView);

            // Wire balloon from pipeline to tray icon
            _pipeline.ShowBalloon += (title, msg) =>
            {
                _trayView?.TrayIcon.ShowBalloonTip(title, msg, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            };

            // Show startup balloon so the user knows the app is alive
            _trayView.TrayIcon.ShowBalloonTip(
                "Prompter is running",
                $"Press and hold {config.HotkeyModifiers} + {config.HotkeyKey} to start dictating.",
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        };
        _trayView.Show();

        // On first run, show a visible welcome window so the user isn't confused
        if (isFirstRun)
        {
            var welcome = new WelcomeWindow(config);
            welcome.ShowDialog();
        }

        // Hide MainWindow
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logger = new FileLogger();
        logger.LogException(e.Exception, "DispatcherUnhandledException");
        e.Handled = true;
        MessageBox.Show(
            $"An unexpected error occurred:\n{e.Exception.Message}\n\nPrompter will continue running, but you may want to restart it.",
            "Prompter Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logger = new FileLogger();
        logger.LogException(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logger = new FileLogger();
        if (e.ExceptionObject is Exception ex)
            logger.LogException(ex, "UnhandledException");
        else
            logger.Log("[ERROR] UnhandledException: " + e.ExceptionObject?.ToString());
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        var logger = new FileLogger();
        if (e.Mode == PowerModes.Resume)
        {
            logger.Log("System resumed from sleep. Re-initializing Foundry Local.");
            _ = Task.Run(async () =>
            {
                try
                {
                    var cfg = new ConfigService().Load();
                    await _foundry?.InitializeAsync(cfg.ModelIdleTtlMinutes)!;
                }
                catch (Exception ex)
                {
                    logger.LogException(ex, "Resume re-init");
                }
            });
        }
        else if (e.Mode == PowerModes.Suspend)
        {
            logger.Log("System suspending. Unloading models.");
            _foundry?.Dispose();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _trayVm?.Dispose();
        _pipeline?.Dispose();
        _recorder?.Dispose();
        _foundry?.Dispose();

        if (_singleInstanceMutex != null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
