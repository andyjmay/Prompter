using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Prompter.Services;
using Prompter.ViewModels;
using Prompter.Views;

namespace Prompter;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance enforcement
        const string mutexName = "Prompter_SingleInstance_Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        // Build DI container
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var logger = _serviceProvider.GetRequiredService<IFileLogger>();
        logger.Log("Prompter starting.");

        var exceptionHandler = _serviceProvider.GetRequiredService<IExceptionHandler>();

        // Global exception handlers
        DispatcherUnhandledException += (_, args) =>
        {
            exceptionHandler.HandleDispatcherException(args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            exceptionHandler.HandleUnobservedTaskException(args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                exceptionHandler.HandleFatalException(ex);
        };
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Sync startup registry
        var configService = _serviceProvider.GetRequiredService<IConfigService>();
        var startupService = _serviceProvider.GetRequiredService<IStartupService>();
        var config = configService.Load();
        if (config.AutoStartWithWindows != startupService.IsEnabled())
        {
            startupService.SetEnabled(config.AutoStartWithWindows);
        }

        // Initialize Foundry in background
        var accessor = _serviceProvider.GetRequiredService<IFoundryLocalManagerAccessor>();
        var modelManager = _serviceProvider.GetRequiredService<IModelManager>();
        _ = Task.Run(async () =>
        {
            try
            {
                await modelManager.InitializeAsync(config.ModelIdleTtlMinutes);
                logger.Log("Foundry Local initialized.");
            }
            catch (Exception ex)
            {
                logger.LogException(ex, "Foundry Local init");
            }
        });

        // Build tray UI
        var trayView = _serviceProvider.GetRequiredService<TrayIconView>();
        var trayVm = _serviceProvider.GetRequiredService<TrayIconViewModel>();
        var eventCoordinator = _serviceProvider.GetRequiredService<AppEventCoordinator>();

        trayView.Loaded += (_, _) =>
        {
            trayView.TrayIcon.DataContext = trayVm;
            trayVm.Initialize();
            eventCoordinator.Initialize();

            // Show startup balloon
            var cfg = configService.Load();
            var hotkeyDisplay = string.IsNullOrEmpty(cfg.HotkeyKey)
                ? cfg.HotkeyModifiers
                : $"{cfg.HotkeyModifiers} + {cfg.HotkeyKey}";
            if (cfg.NotificationsEnabled)
            {
                trayView.TrayIcon.ShowBalloonTip(
                    "Prompter is running",
                    $"Press and hold {hotkeyDisplay} to start dictating.",
                    Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
        };
        trayView.Show();

        // First run
        var firstRunService = _serviceProvider.GetRequiredService<IFirstRunService>();
        _ = firstRunService.CheckAndShowAsync();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_serviceProvider == null) return;

        var logger = _serviceProvider.GetRequiredService<IFileLogger>();
        if (e.Mode == PowerModes.Resume)
        {
            logger.Log("System resumed from sleep. Re-initializing Foundry Local.");
            var modelManager = _serviceProvider.GetRequiredService<IModelManager>();
            var configService = _serviceProvider.GetRequiredService<IConfigService>();
            _ = Task.Run(async () =>
            {
                try
                {
                    var cfg = configService.Load();
                    await modelManager.InitializeAsync(cfg.ModelIdleTtlMinutes);
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
            var modelManager = _serviceProvider.GetRequiredService<IModelManager>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await modelManager.DisposeAsync();
                }
                catch { }
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().Wait();
        }
        else
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }

        if (_singleInstanceMutex != null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core infrastructure
        services.AddSingleton<IFileLogger, FileLogger>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IExceptionHandler, ExceptionHandler>();
        services.AddSingleton<IFirstRunService, FirstRunService>();

        // Foundry layer
        services.AddSingleton<IFoundryLocalManagerAccessor, FoundryLocalManagerAccessor>();
        services.AddSingleton<IModelManager, ModelManager>();
        services.AddSingleton<ITranscriptionService, TranscriptionService>();
        services.AddSingleton<ITextFormatter, TextFormatter>();
        services.AddSingleton<IModelCatalogService, ModelCatalogService>();

        // Pipeline and recording
        services.AddSingleton<IAudioRecorderService, AudioRecorderService>();
        services.AddSingleton<IAudioFeedbackService, AudioFeedbackService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IInputInjectorService, InputInjectorService>();
        services.AddSingleton<IPipelineOrchestrator, PipelineOrchestrator>();
        services.AddTransient<IRecordingUIManager, RecordingUIManager>();

        // Hotkey
        services.AddSingleton<IHotkeyService, HotkeyService>();

        // UI layer
        services.AddSingleton<TrayIconViewModel>();
        services.AddSingleton<TrayIconView>();
        services.AddSingleton<AppEventCoordinator>();
    }
}
