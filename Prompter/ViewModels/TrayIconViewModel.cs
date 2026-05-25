using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.ViewModels;

public class ModeMenuItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;

    public string Id { get; }
    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public ModeMenuItem(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class TrayIconViewModel : IAsyncDisposable, System.ComponentModel.INotifyPropertyChanged
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
    private readonly IHuggingFaceService _hfService;
    private readonly IGgufModelStore _ggufStore;
    private readonly IInputInjectorService _inputInjectorService;
    private AppConfig _config;
    private readonly EventHandler<AppConfig> _onConfigChanged;

    private string? _lastOutput;

    public ObservableCollection<ModeMenuItem> ModeMenuItems { get; } = new();

    public ICommand OpenSettingsCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand SetModeCommand { get; }
    public ICommand CopyLastOutputCommand { get; }

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
        IDialogService dialogService,
        IHuggingFaceService hfService,
        IGgufModelStore ggufStore,
        IInputInjectorService inputInjectorService)
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
        _hfService = hfService;
        _ggufStore = ggufStore;
        _inputInjectorService = inputInjectorService;
        _config = configService.Load();

        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
        SetModeCommand = new RelayCommand(param => SetMode(param?.ToString()));
        CopyLastOutputCommand = new RelayCommand(_ => CopyLastOutput());

        RefreshModeMenuItems();

        _onConfigChanged = (_, cfg) =>
        {
            _config = cfg;
            RefreshModeMenuItems();
            OnPropertyChanged(nameof(CleanEnabled));
            OnPropertyChanged(nameof(ListFormattingEnabled));
        };
        _configService.ConfigChanged += _onConfigChanged;
    }

    private void RefreshModeMenuItems()
    {
        ModeMenuItems.Clear();
        foreach (var mode in _config.Modes)
        {
            ModeMenuItems.Add(new ModeMenuItem(mode.Id, mode.Name)
            {
                IsSelected = mode.Id.Equals(_config.DefaultModeId, StringComparison.OrdinalIgnoreCase)
            });
        }
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
            _textFormatter,
            _hfService,
            _ggufStore,
            _inputInjectorService);

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

    private void SetMode(string? modeId)
    {
        if (string.IsNullOrWhiteSpace(modeId)) return;

        var mode = _config.Modes.FirstOrDefault(m => m.Id.Equals(modeId, StringComparison.OrdinalIgnoreCase));
        if (mode == null) return;

        _config = _config with { DefaultModeId = modeId };
        _ = _configService.SaveAsync(_config);

        foreach (var item in ModeMenuItems)
        {
            item.IsSelected = item.Id.Equals(modeId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool CleanEnabled
    {
        get => _config.CleanEnabled;
        set
        {
            if (_config.CleanEnabled != value)
            {
                _config = _config with { CleanEnabled = value };
                OnPropertyChanged(nameof(CleanEnabled));
                _ = _configService.SaveAsync(_config);
            }
        }
    }

    public bool ListFormattingEnabled
    {
        get => _config.ListFormattingEnabled;
        set
        {
            if (_config.ListFormattingEnabled != value)
            {
                _config = _config with { ListFormattingEnabled = value };
                OnPropertyChanged(nameof(ListFormattingEnabled));
                _ = _configService.SaveAsync(_config);
            }
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
