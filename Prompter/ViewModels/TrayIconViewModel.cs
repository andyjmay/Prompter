using System.Windows;
using System.Windows.Input;
using Prompter.Models;
using Prompter.Services;
using Prompter.Views;

namespace Prompter.ViewModels;

public class TrayIconViewModel : IDisposable
{
    private readonly ConfigService _configService;
    private readonly HotkeyService _hotkeyService;
    private readonly PipelineService _pipelineService;
    private readonly ClipboardService _clipboardService;
    private readonly StartupService _startupService;
    private readonly FileLogger _logger;
    private readonly FoundryOrchestrator _foundry;
    private AppConfig _config;

    private string? _lastOutput;

    public ICommand OpenSettingsCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand SetModeCommand { get; }
    public ICommand CopyLastOutputCommand { get; }

    public bool IsStandard => _config.DefaultMode == FormatMode.Standard;
    public bool IsFormal => _config.DefaultMode == FormatMode.Formal;
    public bool IsRaw => _config.DefaultMode == FormatMode.Raw;
    public bool IsDebug => _config.DefaultMode == FormatMode.Debug;

    public TrayIconViewModel(
        ConfigService configService,
        HotkeyService hotkeyService,
        PipelineService pipelineService,
        ClipboardService clipboardService,
        StartupService startupService,
        FileLogger logger,
        FoundryOrchestrator foundry)
    {
        _configService = configService;
        _hotkeyService = hotkeyService;
        _pipelineService = pipelineService;
        _clipboardService = clipboardService;
        _startupService = startupService;
        _logger = logger;
        _foundry = foundry;
        _config = configService.Load();

        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
        SetModeCommand = new RelayCommand(param => SetMode(param?.ToString()));
        CopyLastOutputCommand = new RelayCommand(_ => CopyLastOutput());

        _hotkeyService.RecordingStarted += OnRecordingStarted;
        _hotkeyService.RecordingStopped += OnRecordingStopped;
    }

    private Window? _hostWindow;

    public void Initialize(Window window)
    {
        _hostWindow = window;
        _hotkeyService.Initialize(window, _config.HotkeyModifiers, _config.HotkeyKey);
    }

    private void OnRecordingStarted()
    {
        _logger.Log("VM: RecordingStarted");
        _pipelineService.StartRecording();
    }

    private void OnRecordingStopped()
    {
        _logger.Log("VM: RecordingStopped");
        _pipelineService.StopRecordingAndProcess(_config.DefaultMode);
    }

    private void OpenSettings()
    {
        var oldKey = _config.HotkeyKey;
        var oldMods = _config.HotkeyModifiers;

        _hotkeyService.Unregister();

        var settings = new SettingsWindow(_configService, _clipboardService, _startupService, _logger, _foundry);
        settings.ShowDialog();
        _config = _configService.Load();

        _logger.Log($"Settings closed. Re-registering hotkey: {_config.HotkeyModifiers}+{_config.HotkeyKey}.");
        if (_hostWindow != null)
        {
            try
            {
                _hotkeyService.Dispose();
                _hotkeyService.Initialize(_hostWindow, _config.HotkeyModifiers, _config.HotkeyKey);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Hotkey re-registration failed");
                MessageBox.Show(
                    $"Could not register the new hotkey '{_config.HotkeyModifiers} + {_config.HotkeyKey}'.\n\nIt may already be in use by another application. Reverting to the previous hotkey.",
                    "Hotkey Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _config.HotkeyKey = oldKey;
                _config.HotkeyModifiers = oldMods;
                _ = _configService.SaveAsync(_config);
                try
                {
                    _hotkeyService.Initialize(_hostWindow, oldMods, oldKey);
                }
                catch (Exception ex2)
                {
                    _logger.LogException(ex2, "Hotkey revert also failed");
                }
            }
        }
    }

    private void SetMode(string? modeName)
    {
        if (Enum.TryParse<FormatMode>(modeName, out var mode))
        {
            _config.DefaultMode = mode;
            _ = _configService.SaveAsync(_config);
            OnPropertyChanged(nameof(IsStandard));
            OnPropertyChanged(nameof(IsFormal));
            OnPropertyChanged(nameof(IsRaw));
            OnPropertyChanged(nameof(IsDebug));
        }
    }

    private void CopyLastOutput()
    {
        if (!string.IsNullOrEmpty(_lastOutput))
        {
            _clipboardService.CopyText(_lastOutput);
        }
    }

    public void SetLastOutput(string text)
    {
        _lastOutput = text;
    }

    public void Dispose()
    {
        _hotkeyService.RecordingStarted -= OnRecordingStarted;
        _hotkeyService.RecordingStopped -= OnRecordingStopped;
        _hotkeyService.Dispose();
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    public RelayCommand(Action<object?> execute) => _execute = execute;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
