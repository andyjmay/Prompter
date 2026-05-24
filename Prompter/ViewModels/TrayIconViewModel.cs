using System.Windows;
using System.Windows.Input;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.ViewModels;

public class TrayIconViewModel : IAsyncDisposable
{
    private readonly IConfigService _configService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IPipelineOrchestrator _pipelineService;
    private readonly IClipboardService _clipboardService;
    private readonly IStartupService _startupService;
    private readonly IFileLogger _logger;
    private readonly IModelCatalogService _modelCatalog;
    private readonly IModelManager _modelManager;
    private readonly ITextFormatter _textFormatter;
    private readonly IDialogService _dialogService;
    private AppConfig _config;
    private readonly EventHandler<AppConfig> _onConfigChanged;

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
        IConfigService configService,
        IHotkeyService hotkeyService,
        IPipelineOrchestrator pipelineService,
        IClipboardService clipboardService,
        IStartupService startupService,
        IFileLogger logger,
        IModelCatalogService modelCatalog,
        IModelManager modelManager,
        ITextFormatter textFormatter,
        IDialogService dialogService)
    {
        _configService = configService;
        _hotkeyService = hotkeyService;
        _pipelineService = pipelineService;
        _clipboardService = clipboardService;
        _startupService = startupService;
        _logger = logger;
        _modelCatalog = modelCatalog;
        _modelManager = modelManager;
        _textFormatter = textFormatter;
        _dialogService = dialogService;
        _config = configService.Load();

        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
        SetModeCommand = new RelayCommand(param => SetMode(param?.ToString()));
        CopyLastOutputCommand = new RelayCommand(_ => CopyLastOutput());

        _onConfigChanged = (_, cfg) => _config = cfg;
        _configService.ConfigChanged += _onConfigChanged;
    }

    public void Initialize()
    {
        _hotkeyService.Initialize(_config.HotkeyModifiers, _config.HotkeyKey);
    }

    private void OpenSettings()
    {
        var oldKey = _config.HotkeyKey;
        var oldMods = _config.HotkeyModifiers;

        _hotkeyService.Unregister();

        bool saved = _dialogService.ShowSettingsDialog(
            _configService,
            _clipboardService,
            _startupService,
            _logger,
            _modelCatalog,
            _modelManager,
            _textFormatter);

        _config = _configService.Load();

        if (saved)
        {
            _logger.Log($"Settings closed. Re-registering hotkey: {_config.HotkeyModifiers}+{_config.HotkeyKey}.");
            try
            {
                _hotkeyService.UpdateHotkey(_config.HotkeyModifiers, _config.HotkeyKey);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Hotkey re-registration failed");
                _dialogService.ShowWarning(
                    "Hotkey Error",
                    $"Could not register the new hotkey '{_config.HotkeyModifiers} + {_config.HotkeyKey}'.\n\nIt may already be in use by another application. Reverting to the previous hotkey.");

                _config = _config with { HotkeyKey = oldKey, HotkeyModifiers = oldMods };
                _ = _configService.SaveAsync(_config);
                try
                {
                    _hotkeyService.UpdateHotkey(oldMods, oldKey);
                }
                catch (Exception ex2)
                {
                    _logger.LogException(ex2, "Hotkey revert also failed");
                }
            }
        }
        else
        {
            try
            {
                _hotkeyService.UpdateHotkey(oldMods, oldKey);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Hotkey re-register after cancel failed");
            }
        }
    }

    private void SetMode(string? modeName)
    {
        if (Enum.TryParse<FormatMode>(modeName, out var mode))
        {
            _config = _config with { DefaultMode = mode };
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

    public async ValueTask DisposeAsync()
    {
        _configService.ConfigChanged -= _onConfigChanged;
        await _hotkeyService.DisposeAsync();
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
