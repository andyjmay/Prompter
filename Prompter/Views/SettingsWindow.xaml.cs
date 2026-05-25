using System.IO;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using DictionaryEntry = Prompter.Models.DictionaryEntry;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views;

public partial class SettingsWindow : Window
{
    private readonly IConfigService _configService;
    private readonly IClipboardService _clipboardService;
    private readonly IStartupService _startupService;
    private readonly IFileLogger _logger;
    private readonly IModelCatalogService _modelCatalog;
    private readonly IModelManager _modelManager;
    private readonly IHuggingFaceService _hfService;
    private readonly IGgufModelStore _ggufStore;
    private AppConfig _config;
    private DispatcherTimer? _captureTimer;
    private bool _finalizing;
    private readonly Dictionary<string, string> _displayNameToAlias = new();
    private readonly Dictionary<string, string> _whisperDisplayNameToAlias = new();
    private string? _activeSortBy;
    private ListSortDirection _activeSortDirection = ListSortDirection.Ascending;
    private RecordingOverlay? _previewOverlay;
    private PreviewToast? _previewToast;
    private readonly ITextFormatter _textFormatter;
    private readonly IInputInjectorService _inputInjectorService;
    private CancellationTokenSource? _testCts;
    private bool _populatingChatModels;
    private bool _populatingWhisperModels;
    private List<LogEntry> _allLogEntries = new();

    public SettingsWindow(
        IConfigService configService,
        IClipboardService clipboardService,
        IStartupService startupService,
        IFileLogger logger,
        IModelCatalogService modelCatalog,
        IModelManager modelManager,
        ITextFormatter textFormatter,
        IHuggingFaceService hfService,
        IGgufModelStore ggufStore,
        IInputInjectorService inputInjectorService)
    {
        InitializeComponent();
        _configService = configService;
        _clipboardService = clipboardService;
        _startupService = startupService;
        _logger = logger;
        _modelCatalog = modelCatalog;
        _modelManager = modelManager;
        _textFormatter = textFormatter;
        _hfService = hfService;
        _ggufStore = ggufStore;
        _inputInjectorService = inputInjectorService;
        _config = configService.Load();

        HotkeyTextBox.Text = string.IsNullOrEmpty(_config.HotkeyKey)
            ? _config.HotkeyModifiers
            : $"{_config.HotkeyModifiers} + {_config.HotkeyKey}";
        TtlSlider.Value = _config.ModelIdleTtlMinutes;
        TtlValue.Text = _config.ModelIdleTtlMinutes.ToString();
        AutoStartCheckBox.IsChecked = _config.AutoStartWithWindows;
        AudioFeedbackCheckBox.IsChecked = _config.AudioFeedbackEnabled;
        NotificationsCheckBox.IsChecked = _config.NotificationsEnabled;
        NotifyOnOutputReadyCheckBox.IsChecked = _config.NotifyOnOutputReady;
        SpokenPunctuationCheckBox.IsChecked = _config.SpokenPunctuationEnabled;
        CleanEnabledCheckBox.IsChecked = _config.CleanEnabled;
        CleanPromptTextBox.Text = _config.CleanPrompt ?? string.Empty;
        UpdateCleanPromptControlsState();
        ListFormattingEnabledCheckBox.IsChecked = _config.ListFormattingEnabled;
        HfTokenTextBox.Text = _config.HuggingFaceToken ?? string.Empty;

        UsePasteCheckBox.IsChecked = _config.UseClipboardPaste;
        PasteThresholdTextBox.Text = _config.PasteThresholdCharacters.ToString();

        TtlSlider.ValueChanged += (_, e) => TtlValue.Text = e.NewValue.ToString("F0");

        RefreshModesList();
        RefreshDictionaryList();
        RefreshSnippetsList();

        _ = PopulateChatModelComboBoxAsync();
        _ = PopulateWhisperModelComboBoxAsync();
        _ = RefreshModelsDashboardAsync();
        DetectGpuStatus();
        InitializeAppearanceControls();
        LoadLogs();
    }

    private void InitializeAppearanceControls()
    {
        foreach (var anchor in Enum.GetValues<OverlayAnchor>())
        {
            RecordingAnchorComboBox.Items.Add(anchor.ToString());
            PreviewAnchorComboBox.Items.Add(anchor.ToString());
        }

        RecordingAnchorComboBox.SelectedItem = _config.RecordingOverlay.Anchor.ToString();
        RecordingOffsetXTextBox.Text = _config.RecordingOverlay.OffsetX.ToString();
        RecordingOffsetYTextBox.Text = _config.RecordingOverlay.OffsetY.ToString();
        ShowRecordingOverlayCheckBox.IsChecked = _config.RecordingOverlay.Enabled;
        ShowAudioMeterCheckBox.IsChecked = _config.RecordingOverlay.ShowAudioLevelMeter;

        PreviewAnchorComboBox.SelectedItem = _config.PreviewToast.Placement.Anchor.ToString();
        PreviewOffsetXTextBox.Text = _config.PreviewToast.Placement.OffsetX.ToString();
        PreviewOffsetYTextBox.Text = _config.PreviewToast.Placement.OffsetY.ToString();
        ShowPreviewToastCheckBox.IsChecked = _config.PreviewToast.Placement.Enabled;
        ToastDurationSlider.Value = _config.PreviewToast.DurationSeconds;
        ToastDurationValue.Text = _config.PreviewToast.DurationSeconds.ToString();

        foreach (var theme in Enum.GetValues<OverlayTheme>())
        {
            ThemeComboBox.Items.Add(theme.ToString());
        }
        ThemeComboBox.SelectedItem = _config.OverlayStyle.Theme.ToString();
        AccentColorTextBox.Text = _config.OverlayStyle.AccentColor ?? string.Empty;
        OpacitySlider.Value = (int)Math.Round(_config.OverlayStyle.BackgroundOpacity * 100);
        OpacityValue.Text = OpacitySlider.Value.ToString("F0") + "%";

        ToastDurationSlider.ValueChanged += (_, e) => ToastDurationValue.Text = e.NewValue.ToString("F0");
        OpacitySlider.ValueChanged += (_, e) =>
        {
            OpacityValue.Text = e.NewValue.ToString("F0") + "%";
            UpdateMockups();
        };
        ThemeComboBox.SelectionChanged += (_, _) => UpdateMockups();
        AccentColorTextBox.TextChanged += (_, _) => UpdateMockups();

        UpdateMockups();
    }

    private void UpdateMockups()
    {
        var style = BuildStyleFromControls();
        var brushes = ThemeResolver.Resolve(style);

        RecordingMockup.Background = brushes.OverlayBackground;
        RecordingMockup.BorderBrush = brushes.OverlayBorder;
        RecordingMockupDot.Fill = brushes.Accent;
        RecordingMockupText.Foreground = brushes.PrimaryText;

        ToastMockup.Background = brushes.ToastBackground;
        ToastMockup.BorderBrush = brushes.ToastBorder;
        ToastMockupTitle.Foreground = brushes.PrimaryText;
        ToastMockupBody.Foreground = brushes.SecondaryText;

        if (!string.IsNullOrWhiteSpace(style.AccentColor) && brushes.Accent is System.Windows.Media.SolidColorBrush sb)
        {
            AccentPreview.Background = new System.Windows.Media.SolidColorBrush(sb.Color);
        }
        else
        {
            AccentPreview.Background = System.Windows.Media.Brushes.Transparent;
        }
    }

    private OverlayStyleConfig BuildStyleFromControls()
    {
        var themeText = ThemeComboBox.SelectedItem as string ?? "Dark";
        var theme = Enum.TryParse<OverlayTheme>(themeText, out var t) ? t : OverlayTheme.Dark;
        var accent = AccentColorTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(accent)) accent = null;
        var opacity = OpacitySlider.Value / 100.0;
        return new OverlayStyleConfig { Theme = theme, AccentColor = accent, BackgroundOpacity = opacity };
    }

    private void RefreshModesList()
    {
        var selectedId = (ModesListView.SelectedItem as ModeListItem)?.Id;

        var items = _config.Modes.Select(m => new ModeListItem
        {
            Id = m.Id,
            Name = m.Name,
            SystemPrompt = m.SystemPrompt,
            SkipFormatting = m.SkipFormatting,
            ShowDiagnosticOutput = m.ShowDiagnosticOutput,
            IsDefault = m.Id.Equals(_config.DefaultModeId, StringComparison.OrdinalIgnoreCase),
            IsBuiltIn = m.IsBuiltIn
        }).ToList();

        ModesListView.ItemsSource = items;

        if (selectedId != null)
        {
            var toSelect = items.FirstOrDefault(i => i.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            if (toSelect != null)
            {
                ModesListView.SelectedItem = toSelect;
                ModesListView.ScrollIntoView(toSelect);
            }
        }
    }

    private void AddModeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ModeEditorDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            if (_config.Modes.Any(m => m.Id.Equals(dialog.Result.Id, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    $"A mode with the ID '{dialog.Result.Id}' already exists. Please choose a different name.",
                    "Duplicate Mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            _config = _config with
            {
                Modes = new List<ModeConfig>(_config.Modes) { dialog.Result }
            };
            RefreshModesList();
        }
    }

    private void EditModeButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModesListView.SelectedItem as ModeListItem;
        if (selected == null)
        {
            MessageBox.Show("Please select a mode from the list first.", "Edit Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mode = _config.Modes.FirstOrDefault(m => m.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
        if (mode == null) return;

        var dialog = new ModeEditorDialog(mode);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var updated = new List<ModeConfig>(_config.Modes);
            var idx = updated.FindIndex(m => m.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                updated[idx] = dialog.Result;
                _config = _config with { Modes = updated };
                RefreshModesList();
            }
        }
    }

    private void SetDefaultModeButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModesListView.SelectedItem as ModeListItem;
        if (selected == null)
        {
            MessageBox.Show("Please select a mode from the list first.", "Set Default", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _config = _config with { DefaultModeId = selected.Id };
        RefreshModesList();
    }

    private void ResetModeButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModesListView.SelectedItem as ModeListItem;
        if (selected == null)
        {
            MessageBox.Show("Please select a mode from the list first.", "Reset Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!selected.IsBuiltIn)
        {
            MessageBox.Show("Only built-in modes can be reset to defaults.", "Reset Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultMode = ModeDefaults.GetById(selected.Id);
        if (defaultMode == null) return;

        var result = MessageBox.Show(
            $"Reset '{selected.Name}' to its default prompt and settings?",
            "Confirm Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var updated = new List<ModeConfig>(_config.Modes);
        var idx = updated.FindIndex(m => m.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            updated[idx] = defaultMode;
            _config = _config with { Modes = updated };
            RefreshModesList();
        }
    }

    private void DeleteModeButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModesListView.SelectedItem as ModeListItem;
        if (selected == null)
        {
            MessageBox.Show("Please select a mode from the list first.", "Delete Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.IsBuiltIn)
        {
            MessageBox.Show("Built-in modes cannot be deleted.", "Delete Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Delete the custom mode '{selected.Name}'? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var updated = _config.Modes.Where(m => !m.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        var newDefaultId = _config.DefaultModeId;
        if (selected.Id.Equals(newDefaultId, StringComparison.OrdinalIgnoreCase))
        {
            newDefaultId = ModeDefaults.StandardId;
        }
        _config = _config with { Modes = updated, DefaultModeId = newDefaultId };
        RefreshModesList();
    }

    private void RefreshDictionaryList()
    {
        var selectedIndex = DictionaryListView.SelectedIndex;
        var selectedWord = (DictionaryListView.SelectedItem as DictionaryListItem)?.Word;

        var items = _config.DictionaryEntries.Select(e => new DictionaryListItem
        {
            Word = e.Word,
            AliasesDisplay = string.Join(", ", e.Aliases ?? new List<string>()),
            Entry = e
        }).ToList();

        DictionaryListView.ItemsSource = items;

        if (selectedIndex >= 0 && selectedIndex < items.Count)
        {
            var toSelect = items[selectedIndex];
            DictionaryListView.SelectedItem = toSelect;
            DictionaryListView.ScrollIntoView(toSelect);
        }
        else if (selectedWord != null)
        {
            var toSelect = items.FirstOrDefault(i => i.Word.Equals(selectedWord, StringComparison.OrdinalIgnoreCase));
            if (toSelect != null)
            {
                DictionaryListView.SelectedItem = toSelect;
                DictionaryListView.ScrollIntoView(toSelect);
            }
        }
    }

    private void AddDictionaryEntryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DictionaryEntryDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            if (_config.DictionaryEntries.Any(d => d.Word.Equals(dialog.Result.Word, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    $"A dictionary entry for the word '{dialog.Result.Word}' already exists. Please edit the existing entry instead.",
                    "Duplicate Entry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            _config = _config with
            {
                DictionaryEntries = new List<DictionaryEntry>(_config.DictionaryEntries) { dialog.Result }
            };
            RefreshDictionaryList();
        }
    }

    private void EditDictionaryEntryButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = DictionaryListView.SelectedItem as DictionaryListItem;
        if (selected == null)
        {
            MessageBox.Show("Please select an entry from the list first.", "Edit Entry", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new DictionaryEntryDialog(selected.Entry);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var duplicate = _config.DictionaryEntries.FirstOrDefault(
                d => d.Word.Equals(dialog.Result.Word, StringComparison.OrdinalIgnoreCase) &&
                     !ReferenceEquals(d, selected.Entry));
            if (duplicate != null)
            {
                MessageBox.Show(
                    $"Another dictionary entry for the word '{dialog.Result.Word}' already exists. Please choose a different word.",
                    "Duplicate Entry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            var updated = new List<DictionaryEntry>(_config.DictionaryEntries);
            var idx = updated.FindIndex(d => ReferenceEquals(d, selected.Entry));
            if (idx >= 0)
            {
                updated[idx] = dialog.Result;
                _config = _config with { DictionaryEntries = updated };
                RefreshDictionaryList();
            }
        }
    }

    private void DeleteDictionaryEntryButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = DictionaryListView.SelectedItem as DictionaryListItem;
        if (selected == null)
        {
            MessageBox.Show("Please select an entry from the list first.", "Delete Entry", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Delete the dictionary entry '{selected.Word}'? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var updated = _config.DictionaryEntries
            .Where(d => !ReferenceEquals(d, selected.Entry))
            .ToList();

        _config = _config with { DictionaryEntries = updated };
        RefreshDictionaryList();
    }

    private void RefreshSnippetsList()
    {
        var selectedTrigger = (SnippetsListView.SelectedItem as SnippetListItem)?.Trigger;

        var items = _config.Snippets.Select(s => new SnippetListItem
        {
            Trigger = s.Trigger,
            ExpansionPreview = s.Expansion.Length > 80 ? s.Expansion[..80] + "…" : s.Expansion,
            Entry = s
        }).ToList();

        SnippetsListView.ItemsSource = items;

        if (selectedTrigger != null)
        {
            var toSelect = items.FirstOrDefault(i => i.Trigger.Equals(selectedTrigger, StringComparison.OrdinalIgnoreCase));
            if (toSelect != null)
            {
                SnippetsListView.SelectedItem = toSelect;
                SnippetsListView.ScrollIntoView(toSelect);
            }
        }
    }

    private void AddSnippetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SnippetDialog(_inputInjectorService, _config.Snippets.ToList());
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _config = _config with
            {
                Snippets = new List<Snippet>(_config.Snippets) { dialog.Result }
            };
            RefreshSnippetsList();
        }
    }

    private void EditSnippetButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = SnippetsListView.SelectedItem as SnippetListItem;
        if (selected == null)
        {
            MessageBox.Show("Please select a snippet from the list first.", "Edit Snippet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SnippetDialog(_inputInjectorService, _config.Snippets.ToList(), selected.Entry);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var updated = new List<Snippet>(_config.Snippets);
            var idx = updated.FindIndex(s => ReferenceEquals(s, selected.Entry));
            if (idx >= 0)
            {
                updated[idx] = dialog.Result;
                _config = _config with { Snippets = updated };
                RefreshSnippetsList();
            }
        }
    }

    private void DeleteSnippetButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = SnippetsListView.SelectedItem as SnippetListItem;
        if (selected == null)
        {
            MessageBox.Show("Please select a snippet from the list first.", "Delete Snippet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Delete the snippet '{selected.Trigger}'? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var updated = _config.Snippets
            .Where(s => !ReferenceEquals(s, selected.Entry))
            .ToList();

        _config = _config with { Snippets = updated };
        RefreshSnippetsList();
    }

    private async Task PopulateChatModelComboBoxAsync()
    {
        _populatingChatModels = true;
        ChatModelComboBox.Items.Clear();
        ChatModelComboBox.Items.Add("Loading models…");
        ChatModelComboBox.IsEnabled = false;
        ChatModelComboBox.SelectedIndex = 0;
        LoadingModelsText.Visibility = Visibility.Visible;
        ChatModelStatusText.Text = "";
        _displayNameToAlias.Clear();

        try
        {
            var models = await _modelCatalog.ListAvailableChatModelsAsync();

            ChatModelComboBox.Items.Clear();
            foreach (var (alias, displayName) in models)
            {
                _displayNameToAlias[displayName] = alias;
                ChatModelComboBox.Items.Add(displayName);
            }

            var ggufDir = _ggufStore.BaseDirectory;
            if (Directory.Exists(ggufDir))
            {
                var files = Directory.GetFiles(ggufDir, "*.gguf", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    var displayName = $"Custom: {name}";
                    _displayNameToAlias[displayName] = file;
                    ChatModelComboBox.Items.Add(displayName);
                }
            }

            ChatModelComboBox.Items.Add(ModelCatalog.OtherOption);
            ChatModelComboBox.IsEnabled = true;
            LoadingModelsText.Visibility = Visibility.Collapsed;

            var configuredAlias = _config.ChatModelId;
            var configuredDisplay = models.FirstOrDefault(m => m.Alias == configuredAlias).DisplayName;
            if (configuredDisplay != null)
            {
                ChatModelComboBox.SelectedItem = configuredDisplay;
            }
            else if (_config.UseCustomChat && !string.IsNullOrWhiteSpace(_config.CustomChatModelPath))
            {
                var customName = Path.GetFileName(_config.CustomChatModelPath);
                var displayName = $"Custom: {customName}";
                if (ChatModelComboBox.Items.Contains(displayName))
                {
                    ChatModelComboBox.SelectedItem = displayName;
                }
                else
                {
                    ChatModelComboBox.SelectedItem = ModelCatalog.OtherOption;
                    CustomModelTextBox.Text = configuredAlias;
                    CustomModelTextBox.Visibility = Visibility.Visible;
                }
            }
            else if (!string.IsNullOrWhiteSpace(configuredAlias))
            {
                ChatModelComboBox.SelectedItem = ModelCatalog.OtherOption;
                CustomModelTextBox.Text = configuredAlias;
                CustomModelTextBox.Visibility = Visibility.Visible;
            }
            else
            {
                if (ChatModelComboBox.Items.Count > 0)
                    ChatModelComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Failed to load chat models from Foundry catalog");
            ChatModelComboBox.Items.Clear();
            ChatModelComboBox.Items.Add(ModelCatalog.OtherOption);
            ChatModelComboBox.IsEnabled = true;
            LoadingModelsText.Visibility = Visibility.Collapsed;
            ChatModelComboBox.SelectedItem = ModelCatalog.OtherOption;
        }
        finally
        {
            _populatingChatModels = false;
        }
    }

    private async Task PopulateWhisperModelComboBoxAsync()
    {
        _populatingWhisperModels = true;
        WhisperModelComboBox.Items.Clear();
        WhisperModelComboBox.Items.Add("Loading models…");
        WhisperModelComboBox.IsEnabled = false;
        WhisperModelComboBox.SelectedIndex = 0;
        WhisperModelStatusText.Text = "";
        _whisperDisplayNameToAlias.Clear();

        try
        {
            var models = await _modelCatalog.ListAvailableWhisperModelsAsync();

            WhisperModelComboBox.Items.Clear();
            foreach (var (alias, displayName) in models)
            {
                _whisperDisplayNameToAlias[displayName] = alias;
                WhisperModelComboBox.Items.Add(displayName);
            }

            var modelsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Prompter", "models", "ggml");

            if (Directory.Exists(modelsDir))
            {
                var files = Directory.GetFiles(modelsDir, "*.bin");
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    var displayName = $"Custom: {name}";
                    _whisperDisplayNameToAlias[displayName] = file;
                    WhisperModelComboBox.Items.Add(displayName);
                }
            }

            WhisperModelComboBox.IsEnabled = true;

            string? configuredDisplay = null;
            if (_config.UseCustomWhisper && !string.IsNullOrWhiteSpace(_config.CustomWhisperModelPath))
            {
                var customName = Path.GetFileName(_config.CustomWhisperModelPath);
                var displayName = $"Custom: {customName}";
                if (WhisperModelComboBox.Items.Contains(displayName))
                {
                    configuredDisplay = displayName;
                }
            }
            else
            {
                configuredDisplay = models.FirstOrDefault(m => m.Alias == _config.WhisperModelId).DisplayName;
            }

            if (configuredDisplay != null)
            {
                WhisperModelComboBox.SelectedItem = configuredDisplay;
            }
            else
            {
                if (WhisperModelComboBox.Items.Count > 0)
                    WhisperModelComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Failed to load Whisper models from catalog");
            WhisperModelComboBox.Items.Clear();
            WhisperModelComboBox.Items.Add("whisper-tiny");
            _whisperDisplayNameToAlias["whisper-tiny"] = "whisper-tiny";
            WhisperModelComboBox.SelectedIndex = 0;
            WhisperModelComboBox.IsEnabled = true;
        }
        finally
        {
            _populatingWhisperModels = false;
        }
    }

    private async Task RefreshModelsDashboardAsync()
    {
        try
        {
            var statusList = await _modelCatalog.GetModelStatusListAsync();

            // Augment with loaded state from ModelManager
            var chatLoaded = _modelManager.LoadedChatModelAlias;
            var whisperLoaded = _modelManager.LoadedWhisperModelAlias;
            var augmented = statusList.Select(s => s with
            {
                IsLoaded = s.Alias.Equals(chatLoaded, StringComparison.OrdinalIgnoreCase)
                        || s.Alias.Equals(whisperLoaded, StringComparison.OrdinalIgnoreCase)
            }).ToList();

            ModelsListView.ItemsSource = augmented;

            if (!string.IsNullOrEmpty(_activeSortBy))
            {
                Sort(_activeSortBy, _activeSortDirection);
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "RefreshModelsDashboardAsync");
        }
    }

    private void DetectGpuStatus()
    {
        GpuStatusTextBlock.Text = "DirectML GPU/NPU acceleration active (ONNX Runtime)";
    }

    private async void DownloadModelButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModelsListView.SelectedItem as ModelStatusInfo;
        if (selected == null)
        {
            MessageBox.Show("Please select a model from the list first.", "Select Model", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DownloadModelButton.IsEnabled = false;
        UnloadModelButton.IsEnabled = false;
        var originalText = DownloadModelButton.Content;
        DownloadModelButton.Content = "Caching...";

        try
        {
            _logger.Log($"Starting manual download for model: {selected.Alias}");
            await _modelManager.DownloadModelAsync(selected.Alias);
            MessageBox.Show($"Model '{selected.DisplayName}' has been successfully downloaded and cached locally.", "Model Downloaded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, $"Failed to download model {selected.Alias}");
            MessageBox.Show($"Failed to download model:\n{ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DownloadModelButton.Content = originalText;
            DownloadModelButton.IsEnabled = true;
            UnloadModelButton.IsEnabled = true;
            _ = RefreshModelsDashboardAsync();
        }
    }

    private async void UnloadModelButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModelsListView.SelectedItem as ModelStatusInfo;
        if (selected == null)
        {
            MessageBox.Show("Please select a model from the list first.", "Select Model", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!selected.IsLoaded)
        {
            MessageBox.Show("This model is not currently loaded in memory.", "Model Not Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _logger.Log($"Manually unloading model: {selected.Alias}");
            await _modelManager.UnloadModelAsync(selected.Alias);
            MessageBox.Show($"Model '{selected.DisplayName}' has been unloaded from system memory.", "Model Unloaded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, $"Failed to unload model {selected.Alias}");
            MessageBox.Show($"Failed to unload model:\n{ex.Message}", "Unload Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _ = RefreshModelsDashboardAsync();
        }
    }

    private void ChatModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populatingChatModels) return;

        var selected = ChatModelComboBox.SelectedItem as string;
        if (selected == null) return;
        if (selected == ModelCatalog.OtherOption)
        {
            CustomModelTextBox.Visibility = Visibility.Visible;
            ChatModelStatusText.Text = "";
        }
        else if (selected.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase))
        {
            CustomModelTextBox.Visibility = Visibility.Collapsed;
            CustomModelTextBox.Text = string.Empty;
            ChatModelStatusText.Text = "Custom model selected";
        }
        else
        {
            CustomModelTextBox.Visibility = Visibility.Collapsed;
            CustomModelTextBox.Text = string.Empty;
            var alias = _displayNameToAlias.TryGetValue(selected, out var a) ? a : selected;
            _ = DownloadModelOnDemandAsync(alias, ChatModelStatusText);
        }
    }

    private void WhisperModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populatingWhisperModels) return;

        var selected = WhisperModelComboBox.SelectedItem as string;
        if (selected == null) return;

        if (selected.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase))
        {
            WhisperModelStatusText.Text = "Custom model selected";
            return;
        }

        var alias = _whisperDisplayNameToAlias.TryGetValue(selected, out var a) ? a : selected;
        _ = DownloadModelOnDemandAsync(alias, WhisperModelStatusText);
    }

    private void ManageCustomModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var managerWin = new CustomModelManagerWindow(_configService, _logger, _hfService, _ggufStore)
        {
            Owner = this
        };
        managerWin.ShowDialog();
        _ = PopulateWhisperModelComboBoxAsync();
        _ = PopulateChatModelComboBoxAsync();
        _ = RefreshModelsDashboardAsync();
    }

    private async Task DownloadModelOnDemandAsync(string? alias, TextBlock statusTextBlock)
    {
        if (string.IsNullOrWhiteSpace(alias)) return;

        try
        {
            bool isCached = await _modelCatalog.IsModelCachedAsync(alias);
            if (isCached)
            {
                Dispatcher.Invoke(() => statusTextBlock.Text = "Cached ✓");
                return;
            }

            Dispatcher.Invoke(() => statusTextBlock.Text = "Downloading…");
            await _modelManager.DownloadModelAsync(alias);
            Dispatcher.Invoke(() =>
            {
                statusTextBlock.Text = "Downloaded ✓";
                _ = RefreshModelsDashboardAsync();
            });
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, $"On-demand download failed for {alias}");
            Dispatcher.Invoke(() => statusTextBlock.Text = "Download failed");
        }
    }

    private void CaptureHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        HotkeyTextBox.Text = "Listening... (press Esc to cancel)";
        CaptureHotkeyButton.IsEnabled = false;

        var capturedMods = new HashSet<string>();
        var stableMods = new HashSet<string>();
        Key? capturedKey = null;
        var lastDisplay = "";
        DateTime? modsStableSince = null;

        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        var endTime = DateTime.Now.AddSeconds(15);

        _captureTimer.Tick += (_, _) =>
        {
            if (DateTime.Now >= endTime && capturedKey == null && stableMods.Count == 0)
            {
                _captureTimer.Stop();
                CaptureHotkeyButton.IsEnabled = true;
                HotkeyTextBox.Text = "No key captured — click to retry";
                return;
            }

            var modsNow = new HashSet<string>();
            if (IsKeyPhysicallyDown(Key.LeftCtrl) || IsKeyPhysicallyDown(Key.RightCtrl)) modsNow.Add("Ctrl");
            if (IsKeyPhysicallyDown(Key.LeftAlt) || IsKeyPhysicallyDown(Key.RightAlt)) modsNow.Add("Alt");
            if (IsKeyPhysicallyDown(Key.LeftShift) || IsKeyPhysicallyDown(Key.RightShift)) modsNow.Add("Shift");
            if (IsKeyPhysicallyDown(Key.LWin) || IsKeyPhysicallyDown(Key.RWin)) modsNow.Add("Win");

            if (IsKeyPhysicallyDown(Key.Escape))
            {
                _captureTimer.Stop();
                CaptureHotkeyButton.IsEnabled = true;
                HotkeyTextBox.Text = "Cancelled — click to retry";
                return;
            }

            capturedMods = modsNow;

            Key? keyNow = null;
            foreach (Key k in Enum.GetValues(typeof(Key)))
            {
                if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None
                    or Key.Escape)
                    continue;

                if (IsKeyPhysicallyDown(k))
                {
                    keyNow = k;
                    break;
                }
            }

            if (keyNow != null)
            {
                capturedKey = keyNow;
                modsStableSince = null;

                var modStr = string.Join("+", capturedMods);
                var display = capturedMods.Count > 0 ? $"{modStr} + {capturedKey}" : capturedKey.ToString()!;

                if (display != lastDisplay)
                {
                    lastDisplay = display;
                    HotkeyTextBox.Text = display;
                }

                if (!_finalizing)
                {
                    _finalizing = true;
                    Task.Delay(300).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _captureTimer.Stop();
                            CaptureHotkeyButton.IsEnabled = true;
                            HotkeyTextBox.Text = lastDisplay;
                            _finalizing = false;
                        });
                    });
                }
            }
            else
            {
                if (capturedMods.Count > 0 && capturedMods.SetEquals(stableMods))
                {
                    if (modsStableSince == null)
                        modsStableSince = DateTime.Now;
                    else if (DateTime.Now - modsStableSince >= TimeSpan.FromMilliseconds(600) && !_finalizing)
                    {
                        _finalizing = true;
                        var stableModStr = string.Join("+", capturedMods);
                        Task.Delay(100).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                _captureTimer.Stop();
                                CaptureHotkeyButton.IsEnabled = true;
                                HotkeyTextBox.Text = stableModStr;
                                _finalizing = false;
                            });
                        });
                        return;
                    }
                }
                else
                {
                    stableMods = new HashSet<string>(capturedMods);
                    modsStableSince = capturedMods.Count > 0 ? DateTime.Now : null;
                }

                var modStr = string.Join("+", capturedMods);
                var hint = capturedMods.Count > 0 ? $"{modStr} + ..." : "Listening... (press Esc to cancel)";
                if (hint != lastDisplay)
                {
                    lastDisplay = hint;
                    HotkeyTextBox.Text = hint;
                }
            }
        };
        _captureTimer.Start();
    }

    private void UpdateCleanPromptControlsState()
    {
        if (CleanPromptTextBox != null)
        {
            CleanPromptTextBox.IsEnabled = CleanEnabledCheckBox.IsChecked == true;
        }
        if (ResetCleanPromptButton != null)
        {
            ResetCleanPromptButton.IsEnabled = CleanEnabledCheckBox.IsChecked == true;
        }
    }

    private void CleanEnabledCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdateCleanPromptControlsState();
    }

    private void ResetCleanPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (CleanPromptTextBox != null)
        {
            CleanPromptTextBox.Text = ModeDefaults.DefaultCleanPrompt;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var parts = HotkeyTextBox.Text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            MessageBox.Show(
                "Please capture a valid hotkey before saving.",
                "Invalid Hotkey",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var knownModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Win", "Ctrl", "Alt", "Shift" };

        if (knownModifiers.Contains(parts[^1]))
        {
            _config = _config with { HotkeyModifiers = string.Join("+", parts), HotkeyKey = "" };
        }
        else
        {
            _config = _config with
            {
                HotkeyKey = parts[^1],
                HotkeyModifiers = string.Join("+", parts[..^1])
            };
        }

        if (IsDangerousShortcut(_config.HotkeyModifiers, _config.HotkeyKey))
        {
            MessageBox.Show(
                "This key combination is reserved by Windows and cannot be used as a global hotkey.\n\nPlease choose a different combination.",
                "Invalid Hotkey",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _config = _config with
        {
            ModelIdleTtlMinutes = (int)TtlSlider.Value,
            AutoStartWithWindows = AutoStartCheckBox.IsChecked == true,
            AudioFeedbackEnabled = AudioFeedbackCheckBox.IsChecked == true,
            NotificationsEnabled = NotificationsCheckBox.IsChecked == true,
            NotifyOnOutputReady = NotifyOnOutputReadyCheckBox.IsChecked == true,
            SpokenPunctuationEnabled = SpokenPunctuationCheckBox.IsChecked == true,
            CleanEnabled = CleanEnabledCheckBox.IsChecked == true,
            CleanPrompt = CleanPromptTextBox.Text.Trim(),
            ListFormattingEnabled = ListFormattingEnabledCheckBox.IsChecked == true
        };

        _config = _config with { UseClipboardPaste = UsePasteCheckBox.IsChecked == true };
        if (int.TryParse(PasteThresholdTextBox.Text, out var threshold))
        {
            _config = _config with { PasteThresholdCharacters = threshold };
        }
        else
        {
            _config = _config with { PasteThresholdCharacters = 150 };
        }

        var selectedWhisperDisplay = WhisperModelComboBox.SelectedItem as string;
        string proposedWhisperAlias = "whisper-tiny";
        bool useCustomWhisper = false;
        string customWhisperPath = "";

        if (selectedWhisperDisplay != null)
        {
            if (_whisperDisplayNameToAlias.TryGetValue(selectedWhisperDisplay, out var pathOrAlias))
            {
                if (pathOrAlias.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    useCustomWhisper = true;
                    customWhisperPath = pathOrAlias;
                }
                else
                {
                    proposedWhisperAlias = pathOrAlias;
                }
            }
            else
            {
                proposedWhisperAlias = selectedWhisperDisplay;
            }
        }

        var oldWhisperModel = _config.WhisperModelId;
        var oldUseCustomWhisper = _config.UseCustomWhisper;
        var oldCustomPath = _config.CustomWhisperModelPath;

        _config = _config with
        {
            WhisperModelId = proposedWhisperAlias,
            UseCustomWhisper = useCustomWhisper,
            CustomWhisperModelPath = customWhisperPath
        };

        var selectedDisplay = ChatModelComboBox.SelectedItem as string ?? ModelCatalog.OtherOption;
        string proposedAlias;
        bool useCustomChat = false;
        string customChatPath = "";

        if (selectedDisplay.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase))
        {
            useCustomChat = true;
            customChatPath = _displayNameToAlias.TryGetValue(selectedDisplay, out var cp) ? cp : "";
            proposedAlias = Path.GetFileName(customChatPath);
        }
        else
        {
            proposedAlias = selectedDisplay == ModelCatalog.OtherOption
                ? CustomModelTextBox.Text.Trim()
                : (_displayNameToAlias.TryGetValue(selectedDisplay, out var ca) ? ca! : selectedDisplay);
        }

        if (string.IsNullOrWhiteSpace(proposedAlias))
        {
            MessageBox.Show(
                "Please select a chat model or enter a custom model alias.",
                "Invalid Model",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!useCustomChat)
        {
            bool isValid = await _modelCatalog.IsModelInCatalogAsync(proposedAlias);
            if (!isValid)
            {
                var result = MessageBox.Show(
                    $"Model '{proposedAlias}' was not found in the Foundry Local catalog. It may not exist, or the catalog may still be loading.\n\nSave anyway?",
                    "Model Not Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                    return;
            }
        }

        _config = _config with
        {
            ChatModelId = proposedAlias,
            UseCustomChat = useCustomChat,
            CustomChatModelPath = customChatPath
        };

        var recordingAnchor = Enum.TryParse<OverlayAnchor>(RecordingAnchorComboBox.SelectedItem as string, out var ra) ? ra : OverlayAnchor.TopCenter;
        var previewAnchor = Enum.TryParse<OverlayAnchor>(PreviewAnchorComboBox.SelectedItem as string, out var pa) ? pa : OverlayAnchor.BottomRight;

        _config = _config with
        {
            RecordingOverlay = new OverlayPlacementConfig
            {
                Anchor = recordingAnchor,
                OffsetX = int.TryParse(RecordingOffsetXTextBox.Text, out var rox) ? rox : 0,
                OffsetY = int.TryParse(RecordingOffsetYTextBox.Text, out var roy) ? roy : 0,
                Enabled = ShowRecordingOverlayCheckBox.IsChecked == true,
                ShowAudioLevelMeter = ShowAudioMeterCheckBox.IsChecked == true
            },
            PreviewToast = new PreviewToastSpecificConfig
            {
                Placement = new OverlayPlacementConfig
                {
                    Anchor = previewAnchor,
                    OffsetX = int.TryParse(PreviewOffsetXTextBox.Text, out var pox) ? pox : 0,
                    OffsetY = int.TryParse(PreviewOffsetYTextBox.Text, out var poy) ? poy : 0,
                    Enabled = ShowPreviewToastCheckBox.IsChecked == true
                },
                DurationSeconds = (int)ToastDurationSlider.Value
            },
            OverlayStyle = BuildStyleFromControls(),
            HuggingFaceToken = HfTokenTextBox.Text.Trim()
        };

        _startupService.SetEnabled(_config.AutoStartWithWindows);
        await _configService.SaveAsync(_config);

        try
        {
            await _modelManager.UnloadChatModelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "UnloadChatModelAsync on settings save");
        }

        if (proposedWhisperAlias != oldWhisperModel || useCustomWhisper != oldUseCustomWhisper || customWhisperPath != oldCustomPath)
        {
            try
            {
                await _modelManager.UnloadWhisperModelAsync();
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "UnloadWhisperModelAsync on settings save");
            }
        }

        DialogResult = true;
        Close();
    }

    private static bool IsDangerousShortcut(string modifiers, string key)
    {
        var mods = modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasWin = mods.Any(m => m.Equals("Win", StringComparison.OrdinalIgnoreCase));
        var hasCtrl = mods.Any(m => m.Equals("Ctrl", StringComparison.OrdinalIgnoreCase));
        var hasAlt = mods.Any(m => m.Equals("Alt", StringComparison.OrdinalIgnoreCase));
        var hasShift = mods.Any(m => m.Equals("Shift", StringComparison.OrdinalIgnoreCase));

        var k = key.ToUpperInvariant();

        if (hasWin && !hasCtrl && !hasAlt && !hasShift && string.IsNullOrEmpty(k)) return true;

        if (hasWin && !hasCtrl && !hasAlt && !hasShift && !string.IsNullOrEmpty(k))
        {
            var winReserved = new[] { "L", "R", "E", "I", "X", "TAB", "A", "S", "Q", "P", "D", "M", "N", "U", "V", "SHIFT", "C", "Z", "J", "H", "K", "G", "T", "COMMA", "PERIOD", "NUMLOCK", "PAUSE", "BREAK", "UP", "DOWN", "LEFT", "RIGHT", "HOME", "END", "PGUP", "PGDN", "INSERT", "DELETE", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "PRINTSCREEN", "SNAPSHOT", "SNAP", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            if (winReserved.Contains(k)) return true;
        }

        if (hasCtrl && hasAlt && !hasShift && (k == "DELETE" || k == "DEL")) return true;

        if (hasAlt && !hasCtrl && !hasShift && !hasWin && !string.IsNullOrEmpty(k))
        {
            var altReserved = new[] { "TAB", "ESC", "F4" };
            if (altReserved.Contains(k)) return true;
        }

        if (hasCtrl && hasAlt && !hasShift && !hasWin && k == "TAB") return true;

        if (!hasWin && !hasCtrl && !hasAlt && !hasShift && (k == "PRINTSCREEN" || k == "SNAPSHOT" || k == "SNAP")) return true;

        return false;
    }

    private static bool IsKeyPhysicallyDown(Key key)
    {
        int vk = KeyInterop.VirtualKeyFromKey(key);
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        _previewOverlay?.Close();
        _previewOverlay = null;
        _previewToast?.Close();
        _previewToast = null;

        var style = BuildStyleFromControls();

        var recordingAnchor = Enum.TryParse<OverlayAnchor>(RecordingAnchorComboBox.SelectedItem as string, out var ra) ? ra : OverlayAnchor.TopCenter;
        var recordingPlacement = new OverlayPlacementConfig
        {
            Anchor = recordingAnchor,
            OffsetX = int.TryParse(RecordingOffsetXTextBox.Text, out var rox) ? rox : 0,
            OffsetY = int.TryParse(RecordingOffsetYTextBox.Text, out var roy) ? roy : 0,
            Enabled = true
        };

        var previewAnchor = Enum.TryParse<OverlayAnchor>(PreviewAnchorComboBox.SelectedItem as string, out var pa) ? pa : OverlayAnchor.BottomRight;
        var previewPlacement = new OverlayPlacementConfig
        {
            Anchor = previewAnchor,
            OffsetX = int.TryParse(PreviewOffsetXTextBox.Text, out var pox) ? pox : 0,
            OffsetY = int.TryParse(PreviewOffsetYTextBox.Text, out var poy) ? poy : 0,
            Enabled = true
        };

        var previewToastConfig = new PreviewToastSpecificConfig
        {
            Placement = previewPlacement,
            DurationSeconds = (int)ToastDurationSlider.Value
        };

        _previewOverlay = new RecordingOverlay(recordingPlacement, style);
        _previewOverlay.Show();

        _previewToast = new PreviewToast(
            "This is a preview of how your transcribed text will appear.",
            _clipboardService,
            previewToastConfig,
            style);
        _previewToast.Show();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _testCts?.Cancel();
        _previewOverlay?.Close();
        _previewOverlay = null;
        _previewToast?.Close();
        _previewToast = null;
    }

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        var headerClicked = e.OriginalSource as GridViewColumnHeader;
        if (headerClicked == null || headerClicked.Column == null)
            return;

        string? headerText = headerClicked.Column.Header as string;
        if (string.IsNullOrEmpty(headerText))
            return;

        string? sortBy = GetSortPropertyByHeader(headerText);
        if (string.IsNullOrEmpty(sortBy))
            return;

        ListSortDirection direction;
        if (_activeSortBy == sortBy)
        {
            direction = _activeSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            direction = ListSortDirection.Ascending;
        }

        _activeSortBy = sortBy;
        _activeSortDirection = direction;

        Sort(sortBy, direction);
        UpdateHeaderSymbols(headerClicked, direction);
    }

    private string? GetSortPropertyByHeader(string headerText)
    {
        string cleanText = headerText.Replace(" ▲", "").Replace(" ▼", "").Trim();
        return cleanText switch
        {
            "Model Alias" => "Alias",
            "Type" => "TaskType",
            "Size" => "SizeInMegabytes",
            "Downloaded" => "IsCached",
            "Loaded" => "IsLoaded",
            _ => null
        };
    }

    private void Sort(string sortBy, ListSortDirection direction)
    {
        var dataView = CollectionViewSource.GetDefaultView(ModelsListView.ItemsSource);
        if (dataView is ListCollectionView listView)
        {
            listView.SortDescriptions.Clear();
            if (sortBy == "SizeInMegabytes")
            {
                listView.CustomSort = new SizeInMegabytesComparer(direction);
            }
            else
            {
                listView.CustomSort = null;
                listView.SortDescriptions.Add(new SortDescription(sortBy, direction));
            }
            listView.Refresh();
        }
        else if (dataView != null)
        {
            dataView.SortDescriptions.Clear();
            dataView.SortDescriptions.Add(new SortDescription(sortBy, direction));
            dataView.Refresh();
        }
    }

    private void UpdateHeaderSymbols(GridViewColumnHeader clickedHeader, ListSortDirection direction)
    {
        if (ModelsListView.View is GridView gridView)
        {
            foreach (var column in gridView.Columns)
            {
                if (column.Header is string headerText)
                {
                    headerText = headerText.Replace(" ▲", "").Replace(" ▼", "");

                    if (column == clickedHeader.Column)
                    {
                        headerText += (direction == ListSortDirection.Ascending) ? " ▲" : " ▼";
                    }

                    column.Header = headerText;
                }
            }
        }
    }

    private void LoadLogs()
    {
        try
        {
            _allLogEntries = _logger.GetRecentLogs(maxEntries: 5000).ToList();
            LogsListView.ItemsSource = _allLogEntries;
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "LoadLogs");
        }
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to clear all logs? This action cannot be undone.",
            "Clear Logs",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        _logger.ClearLogs();
        _allLogEntries.Clear();
        LogsListView.ItemsSource = null;
        LogsFilterTextBox.Text = "";
    }

    private void LogsFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LogsListView.ItemsSource == null)
            return;

        var view = CollectionViewSource.GetDefaultView(LogsListView.ItemsSource);
        if (view == null)
            return;

        var filterText = LogsFilterTextBox.Text.Trim();
        if (string.IsNullOrEmpty(filterText))
        {
            view.Filter = null;
        }
        else
        {
            view.Filter = obj =>
            {
                if (obj is not LogEntry entry)
                    return false;

                return entry.Message.Contains(filterText, StringComparison.OrdinalIgnoreCase)
                    || entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture).Contains(filterText, StringComparison.OrdinalIgnoreCase)
                    || entry.SourceFile.Contains(filterText, StringComparison.OrdinalIgnoreCase);
            };
        }

        view.Refresh();
    }

    private async void TestModelButton_Click(object sender, RoutedEventArgs e)
    {
        TestModelButton.IsEnabled = false;
        TestResultTextBlock.Visibility = Visibility.Visible;
        TestErrorIndicator.Visibility = Visibility.Collapsed;
        TestResultTextBlock.Text = "Running...";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _testCts?.Dispose();
        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        var selectedDisplay = ChatModelComboBox.SelectedItem as string ?? ModelCatalog.OtherOption;
        var alias = selectedDisplay == ModelCatalog.OtherOption
            ? CustomModelTextBox.Text.Trim()
            : (_displayNameToAlias.TryGetValue(selectedDisplay, out var ta) ? ta! : selectedDisplay);

        if (string.IsNullOrWhiteSpace(alias))
        {
            TestResultTextBlock.Visibility = Visibility.Visible;
            TestErrorIndicator.Visibility = Visibility.Collapsed;
            TestResultTextBlock.Text = "Please select a chat model first.";
            TestModelButton.IsEnabled = true;
            return;
        }

        try
        {
            TestResultTextBlock.Text = $"Loading {alias}...";
            await _modelManager.EnsureChatModelLoadedAsync(alias);

            var sample = TestSampleTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(sample))
            {
                sample = "The quik brown fox jumps over the lazzy dog.";
                TestSampleTextBox.Text = sample;
            }

            TestResultTextBlock.Text = $"Testing {alias}...";
            var result = await _textFormatter.CleanupAsync(sample, ModeDefaults.StandardId, ct);
            var elapsed = sw.Elapsed;

            TestResultTextBlock.Text = $"Model: {alias}\nTime: {elapsed.TotalSeconds:F2}s\nOutput: {result}";
        }
        catch (OperationCanceledException)
        {
            TestResultTextBlock.Text = "Test cancelled.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogException(ex, "Model speed test failed");
            var msg = ex.Message.Contains("Could not load chat model", StringComparison.OrdinalIgnoreCase)
                ? $"Model '{alias}' is not available in the catalog. Please select a valid model from the dropdown and try again."
                : ex.Message;
            TestResultTextBlock.Visibility = Visibility.Collapsed;
            TestErrorIndicator.Visibility = Visibility.Visible;
            TestErrorToolTipText.Text = $"Error after {sw.Elapsed.TotalSeconds:F2}s: {msg}";
        }
        finally
        {
            _testCts?.Dispose();
            _testCts = null;
            try
            {
                TestModelButton.IsEnabled = true;
            }
            catch (InvalidOperationException)
            {
                // Window may be closing; ignore.
            }
        }
    }
}

public class ModeListItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public bool SkipFormatting { get; set; }
    public bool ShowDiagnosticOutput { get; set; }
    public bool IsDefault { get; set; }
    public bool IsBuiltIn { get; set; }
}

public class DictionaryListItem
{
    public string Word { get; set; } = "";
    public string AliasesDisplay { get; set; } = "";
    public DictionaryEntry Entry { get; set; } = new();
}

public class SnippetListItem
{
    public string Trigger { get; set; } = "";
    public string ExpansionPreview { get; set; } = "";
    public Snippet Entry { get; set; } = new();
}

public class BoolToTextConverter : System.Windows.Data.IValueConverter
{
    public string TrueText { get; set; } = "Yes";
    public string FalseText { get; set; } = "No";

    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b) return b ? TrueText : FalseText;
        return FalseText;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new System.NotImplementedException();
}

public class BoolToBrushConverter : System.Windows.Data.IValueConverter
{
    public System.Windows.Media.Brush TrueBrush { get; set; } = System.Windows.Media.Brushes.Green;
    public System.Windows.Media.Brush FalseBrush { get; set; } = System.Windows.Media.Brushes.Red;

    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
        {
            if (b) return TrueBrush;
            return FalseBrush;
        }
        return FalseBrush;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new System.NotImplementedException();
}

public class SizeInMegabytesComparer : IComparer
{
    private readonly ListSortDirection _direction;
    public SizeInMegabytesComparer(ListSortDirection direction) => _direction = direction;

    public int Compare(object? x, object? y)
    {
        if (x is not ModelStatusInfo a || y is not ModelStatusInfo b)
            return 0;

        var av = a.SizeInMegabytes ?? float.MaxValue;
        var bv = b.SizeInMegabytes ?? float.MaxValue;
        var result = av.CompareTo(bv);
        return _direction == ListSortDirection.Ascending ? result : -result;
    }
}
