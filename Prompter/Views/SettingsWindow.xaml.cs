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
    private RecordingOverlay? _previewOverlay;
    private PreviewToast? _previewToast;
    private readonly ITextFormatter _textFormatter;
    private readonly IInputInjectorService _inputInjectorService;
    private readonly IDialogService _dialogService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IAudioRecorderService _audioRecorderService;
    private readonly IThemeService _themeService;
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
        IInputInjectorService inputInjectorService,
        ITranscriptionService transcriptionService,
        IAudioRecorderService audioRecorderService,
        IDialogService dialogService,
        IThemeService themeService)
    {
        InitializeComponent();
        ShowSection(0);
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
        _transcriptionService = transcriptionService;
        _audioRecorderService = audioRecorderService;
        _dialogService = dialogService;
        _themeService = themeService;
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
        _ = LoadLogsAsync();
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
        MaxWidthSlider.Value = _config.PreviewToast.MaxWidth;
        MaxWidthValue.Text = _config.PreviewToast.MaxWidth.ToString();

        foreach (var theme in Enum.GetValues<OverlayTheme>())
        {
            ThemeComboBox.Items.Add(theme.ToString());
        }
        ThemeComboBox.SelectedItem = _config.OverlayStyle.Theme.ToString();
        AccentColorTextBox.Text = _config.OverlayStyle.AccentColor ?? string.Empty;
        TextColorTextBox.Text = _config.OverlayStyle.TextColor ?? string.Empty;
        ProcessingAccentColorTextBox.Text = _config.OverlayStyle.ProcessingAccentColor ?? string.Empty;
        OverlayBackgroundColorTextBox.Text = _config.OverlayStyle.OverlayBackgroundColor ?? string.Empty;
        ToastBackgroundColorTextBox.Text = _config.OverlayStyle.ToastBackgroundColor ?? string.Empty;

        var systemFonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var font in systemFonts)
        {
            FontFamilyComboBox.Items.Add(font);
        }

        var currentFont = _config.OverlayStyle.FontFamily;
        if (!string.IsNullOrWhiteSpace(currentFont) && !systemFonts.Any(f => f.Equals(currentFont, StringComparison.OrdinalIgnoreCase)))
        {
            FontFamilyComboBox.Items.Add(currentFont);
        }
        FontFamilyComboBox.Text = currentFont;

        OverlayFontSizeSlider.Value = _config.OverlayStyle.OverlayFontSize;
        OverlayFontSizeValue.Text = _config.OverlayStyle.OverlayFontSize.ToString();
        ToastTitleFontSizeSlider.Value = _config.OverlayStyle.ToastTitleFontSize;
        ToastTitleFontSizeValue.Text = _config.OverlayStyle.ToastTitleFontSize.ToString();
        ToastBodyFontSizeSlider.Value = _config.OverlayStyle.ToastBodyFontSize;
        ToastBodyFontSizeValue.Text = _config.OverlayStyle.ToastBodyFontSize.ToString();

        OpacitySlider.Value = (int)Math.Round(_config.OverlayStyle.BackgroundOpacity * 100);
        OpacityValue.Text = OpacitySlider.Value.ToString("F0") + "%";
        ToastOpacitySlider.Value = (int)Math.Round(_config.OverlayStyle.ToastOpacity * 100);
        ToastOpacityValue.Text = ToastOpacitySlider.Value.ToString("F0") + "%";
        CornerRadiusSlider.Value = _config.OverlayStyle.CornerRadius;
        CornerRadiusValue.Text = _config.OverlayStyle.CornerRadius.ToString();
        PaddingSlider.Value = _config.OverlayStyle.Padding;
        PaddingValue.Text = _config.OverlayStyle.Padding.ToString();

        ShadowEnabledCheckBox.IsChecked = _config.OverlayStyle.ShadowEnabled;
        ShowStatusTextCheckBox.IsChecked = _config.OverlayStyle.ShowStatusText;
        ListeningLabelTextBox.Text = _config.OverlayStyle.ListeningLabel;
        ProcessingLabelTextBox.Text = _config.OverlayStyle.ProcessingLabel;

        foreach (var speed in Enum.GetValues<OverlayPulseSpeed>())
        {
            PulseSpeedComboBox.Items.Add(speed.ToString());
        }
        PulseSpeedComboBox.SelectedItem = _config.OverlayStyle.PulseSpeed.ToString();

        ToastDurationSlider.ValueChanged += (_, e) => ToastDurationValue.Text = e.NewValue.ToString("F0");
        MaxWidthSlider.ValueChanged += (_, e) => MaxWidthValue.Text = e.NewValue.ToString("F0");
        OpacitySlider.ValueChanged += (_, e) =>
        {
            OpacityValue.Text = e.NewValue.ToString("F0") + "%";
            UpdateMockups();
        };
        ToastOpacitySlider.ValueChanged += (_, e) =>
        {
            ToastOpacityValue.Text = e.NewValue.ToString("F0") + "%";
            UpdateMockups();
        };
        CornerRadiusSlider.ValueChanged += (_, e) =>
        {
            CornerRadiusValue.Text = e.NewValue.ToString("F0");
            UpdateMockups();
        };
        PaddingSlider.ValueChanged += (_, e) =>
        {
            PaddingValue.Text = e.NewValue.ToString("F0");
            UpdateMockups();
        };
        OverlayFontSizeSlider.ValueChanged += (_, e) =>
        {
            OverlayFontSizeValue.Text = e.NewValue.ToString("F0");
            UpdateMockups();
        };
        ToastTitleFontSizeSlider.ValueChanged += (_, e) =>
        {
            ToastTitleFontSizeValue.Text = e.NewValue.ToString("F0");
            UpdateMockups();
        };
        ToastBodyFontSizeSlider.ValueChanged += (_, e) =>
        {
            ToastBodyFontSizeValue.Text = e.NewValue.ToString("F0");
            UpdateMockups();
        };

        ThemeComboBox.SelectionChanged += (_, _) => UpdateMockups();
        AccentColorTextBox.TextChanged += (_, _) => UpdateMockups();
        TextColorTextBox.TextChanged += (_, _) => UpdateMockups();
        ProcessingAccentColorTextBox.TextChanged += (_, _) => UpdateMockups();
        OverlayBackgroundColorTextBox.TextChanged += (_, _) => UpdateMockups();
        ToastBackgroundColorTextBox.TextChanged += (_, _) => UpdateMockups();
        FontFamilyComboBox.SelectionChanged += (_, _) => UpdateMockups();
        FontFamilyComboBox.LostFocus += (_, _) => UpdateMockups();
        ShadowEnabledCheckBox.Checked += (_, _) => UpdateMockups();
        ShadowEnabledCheckBox.Unchecked += (_, _) => UpdateMockups();
        ShowStatusTextCheckBox.Checked += (_, _) => UpdateMockups();
        ShowStatusTextCheckBox.Unchecked += (_, _) => UpdateMockups();
        ListeningLabelTextBox.TextChanged += (_, _) => UpdateMockups();
        ProcessingLabelTextBox.TextChanged += (_, _) => UpdateMockups();
        PulseSpeedComboBox.SelectionChanged += (_, _) => UpdateMockups();

        UpdateMockups();
    }

    private void UpdateMockups()
    {
        var style = BuildStyleFromControls();
        var brushes = ThemeResolver.Resolve(style);

        RecordingMockup.Background = brushes.OverlayBackground;
        RecordingMockup.BorderBrush = brushes.OverlayBorder;
        RecordingMockup.CornerRadius = new System.Windows.CornerRadius(style.CornerRadius);
        RecordingMockup.Padding = new System.Windows.Thickness(style.Padding);
        RecordingMockupDot.Fill = brushes.Accent;
        RecordingMockupText.Foreground = brushes.PrimaryText;
        RecordingMockupText.FontFamily = new System.Windows.Media.FontFamily(style.FontFamily);
        RecordingMockupText.FontSize = style.OverlayFontSize;
        RecordingMockupText.Text = style.ListeningLabel;

        ToastMockup.Background = brushes.ToastBackground;
        ToastMockup.BorderBrush = brushes.ToastBorder;
        ToastMockup.CornerRadius = new System.Windows.CornerRadius(style.CornerRadius);
        ToastMockup.Padding = new System.Windows.Thickness(style.Padding);
        ToastMockupTitle.Foreground = brushes.PrimaryText;
        ToastMockupTitle.FontFamily = new System.Windows.Media.FontFamily(style.FontFamily);
        ToastMockupTitle.FontSize = style.ToastTitleFontSize;
        ToastMockupBody.Foreground = brushes.SecondaryText;
        ToastMockupBody.FontFamily = new System.Windows.Media.FontFamily(style.FontFamily);
        ToastMockupBody.FontSize = style.ToastBodyFontSize;

        if (style.ShowStatusText)
        {
            RecordingMockupText.Visibility = System.Windows.Visibility.Visible;
            RecordingMockupDot.Margin = new System.Windows.Thickness(0, 0, 8, 0);
        }
        else
        {
            RecordingMockupText.Visibility = System.Windows.Visibility.Collapsed;
            RecordingMockupDot.Margin = new System.Windows.Thickness(0);
        }

        if (style.ShadowEnabled)
        {
            RecordingMockup.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                Opacity = 0.3,
                BlurRadius = 12,
                ShadowDepth = 4
            };
            ToastMockup.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                Opacity = 0.3,
                BlurRadius = 12,
                ShadowDepth = 4
            };
        }
        else
        {
            RecordingMockup.Effect = null;
            ToastMockup.Effect = null;
        }

        static System.Windows.Media.Brush GetPreviewBrush(string? customValue, System.Windows.Media.Brush fallback)
        {
            if (string.IsNullOrWhiteSpace(customValue)) return fallback;
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(customValue);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                return fallback;
            }
        }

        AccentPreview.Background = GetPreviewBrush(style.AccentColor, brushes.Accent);
        TextColorPreview.Background = GetPreviewBrush(style.TextColor, brushes.PrimaryText);
        ProcessingAccentPreview.Background = GetPreviewBrush(style.ProcessingAccentColor, brushes.ProcessingAccent);
        OverlayBackgroundPreview.Background = GetPreviewBrush(style.OverlayBackgroundColor, brushes.OverlayBackground);
        ToastBackgroundPreview.Background = GetPreviewBrush(style.ToastBackgroundColor, brushes.ToastBackground);
    }

    private static bool IsValidHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            _ = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private OverlayStyleConfig BuildStyleFromControls()
    {
        var themeText = ThemeComboBox.SelectedItem as string ?? "Dark";
        var theme = Enum.TryParse<OverlayTheme>(themeText, out var t) ? t : OverlayTheme.Dark;
        var accent = IsValidHexColor(AccentColorTextBox.Text) ? AccentColorTextBox.Text.Trim() : null;
        var textColor = IsValidHexColor(TextColorTextBox.Text) ? TextColorTextBox.Text.Trim() : null;
        var processingAccent = IsValidHexColor(ProcessingAccentColorTextBox.Text) ? ProcessingAccentColorTextBox.Text.Trim() : null;
        var overlayBg = IsValidHexColor(OverlayBackgroundColorTextBox.Text) ? OverlayBackgroundColorTextBox.Text.Trim() : null;
        var toastBg = IsValidHexColor(ToastBackgroundColorTextBox.Text) ? ToastBackgroundColorTextBox.Text.Trim() : null;

        var pulseSpeedText = PulseSpeedComboBox.SelectedItem as string ?? "Normal";
        var pulseSpeed = Enum.TryParse<OverlayPulseSpeed>(pulseSpeedText, out var ps) ? ps : OverlayPulseSpeed.Normal;

        var opacity = OpacitySlider.Value / 100.0;
        var toastOpacity = ToastOpacitySlider.Value / 100.0;

        return new OverlayStyleConfig
        {
            Theme = theme,
            AccentColor = accent,
            TextColor = textColor,
            ProcessingAccentColor = processingAccent,
            OverlayBackgroundColor = overlayBg,
            ToastBackgroundColor = toastBg,
            FontFamily = string.IsNullOrWhiteSpace(FontFamilyComboBox.Text) ? "Segoe UI" : FontFamilyComboBox.Text.Trim(),
            OverlayFontSize = (int)OverlayFontSizeSlider.Value,
            ToastTitleFontSize = (int)ToastTitleFontSizeSlider.Value,
            ToastBodyFontSize = (int)ToastBodyFontSizeSlider.Value,
            ShowStatusText = ShowStatusTextCheckBox.IsChecked == true,
            ListeningLabel = ListeningLabelTextBox.Text.Trim(),
            ProcessingLabel = ProcessingLabelTextBox.Text.Trim(),
            BackgroundOpacity = opacity,
            ToastOpacity = toastOpacity,
            CornerRadius = (int)CornerRadiusSlider.Value,
            Padding = (int)PaddingSlider.Value,
            ShadowEnabled = ShadowEnabledCheckBox.IsChecked == true,
            PulseSpeed = pulseSpeed
        };
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
        ModesEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
        DictionaryEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
        SnippetsEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
            _displayNameToAlias["None"] = "none";
            ChatModelComboBox.Items.Add("None");

            foreach (var (alias, displayName, _) in models)
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
            var configuredDisplay = models.FirstOrDefault(m => m.Alias.Equals(configuredAlias, StringComparison.OrdinalIgnoreCase)).DisplayName;
            if (string.Equals(configuredAlias, "none", StringComparison.OrdinalIgnoreCase))
            {
                ChatModelComboBox.SelectedItem = "None";
            }
            else if (configuredDisplay != null)
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
                configuredDisplay = models.FirstOrDefault(m => m.Alias.Equals(_config.WhisperModelId, StringComparison.OrdinalIgnoreCase)).DisplayName;
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
            ModelsEmptyText.Visibility = augmented.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "RefreshModelsDashboardAsync");
        }
    }

    private void DetectGpuStatus()
    {
        // Foundry Local abstracts the execution provider; we cannot query GPU availability
        // without loading a model session. Show a neutral message instead of implying
        // acceleration is definitely available.
        GpuStatusTextBlock.Text = "ONNX Runtime with DirectML (CPU default; GPU/NPU if available)";
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
        if (selected == "None")
        {
            CustomModelTextBox.Visibility = Visibility.Collapsed;
            CustomModelTextBox.Text = string.Empty;
            ChatModelStatusText.Text = "Correction bypassed.";
        }
        else if (selected == ModelCatalog.OtherOption)
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

        if (_captureTimer != null)
        {
            _captureTimer.Stop();
            _captureTimer = null;
        }

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

    private void PasteThresholdTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PasteThresholdTextBox.Text, out var threshold) || threshold < 0)
        {
            PasteThresholdTextBox.Text = "150";
            PasteThresholdTextBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            return;
        }
        PasteThresholdTextBox.Text = Math.Clamp(threshold, 0, 9999).ToString();
        PasteThresholdTextBox.ClearValue(TextBox.BorderBrushProperty);
    }

    private void OffsetTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (!int.TryParse(tb.Text, out var value))
        {
            tb.Text = "0";
            tb.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            return;
        }
        tb.Text = value.ToString();
        tb.ClearValue(TextBox.BorderBrushProperty);
    }

    private void FontFamilyComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var text = FontFamilyComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            FontFamilyComboBox.Text = "Segoe UI";
            return;
        }

        var exists = System.Windows.Media.Fonts.SystemFontFamilies
            .Any(f => f.Source.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            FontFamilyComboBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
        }
        else
        {
            FontFamilyComboBox.ClearValue(ComboBox.BorderBrushProperty);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
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
            bool allModifiers = parts.All(p => knownModifiers.Contains(p));

            string key;
            string modifiers;
            if (allModifiers)
            {
                // Modifier-only hotkey (supported by HotkeyService)
                key = "";
                modifiers = string.Join("+", parts);
            }
            else
            {
                key = parts[^1];
                modifiers = string.Join("+", parts[..^1]);
            }

            _config = _config with
            {
                HotkeyKey = key,
                HotkeyModifiers = modifiers
            };

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
            _config = _config with { PasteThresholdCharacters = Math.Clamp(threshold, 0, 9999) };
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

        if (!useCustomChat && !string.Equals(proposedAlias, "none", StringComparison.OrdinalIgnoreCase))
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

        var oldChatModelId = _config.ChatModelId;
        var oldUseCustomChat = _config.UseCustomChat;
        var oldCustomChatPath = _config.CustomChatModelPath;

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
                DurationSeconds = (int)ToastDurationSlider.Value,
                MaxWidth = (int)MaxWidthSlider.Value
            },
            OverlayStyle = BuildStyleFromControls(),
            HuggingFaceToken = HfTokenTextBox.Text.Trim()
        };

        _startupService.SetEnabled(_config.AutoStartWithWindows);
        await _configService.SaveAsync(_config);
        _themeService.ApplyTheme(_config.OverlayStyle.Theme);

        if (proposedAlias != oldChatModelId || useCustomChat != oldUseCustomChat || customChatPath != oldCustomChatPath)
        {
            try
            {
                await _modelManager.UnloadChatModelAsync();
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "UnloadChatModelAsync on settings save");
            }
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
        catch (Exception ex)
        {
            _logger.LogException(ex, "Save settings failed");
            MessageBox.Show(
                $"Failed to save settings:\n{ex.Message}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
            var winReserved = new[] { "L", "R", "E", "I", "X", "TAB", "A", "S", "Q", "P", "D", "M", "N", "U", "V", "SHIFT", "C", "Z", "J", "H", "K", "G", "T", "OEMCOMMA", "OEMPERIOD", "NUMLOCK", "PAUSE", "UP", "DOWN", "LEFT", "RIGHT", "HOME", "END", "PAGEUP", "PAGEDOWN", "INSERT", "DELETE", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "SNAPSHOT", "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9" };
            if (winReserved.Contains(k)) return true;
        }

        if (hasCtrl && hasAlt && !hasShift && k == "DELETE") return true;

        if (hasAlt && !hasCtrl && !hasShift && !hasWin && !string.IsNullOrEmpty(k))
        {
            var altReserved = new[] { "TAB", "ESCAPE", "F4" };
            if (altReserved.Contains(k)) return true;
        }

        if (hasCtrl && hasAlt && !hasShift && !hasWin && k == "TAB") return true;

        if (!hasWin && !hasCtrl && !hasAlt && !hasShift && k == "SNAPSHOT") return true;

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
        try
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
                DurationSeconds = (int)ToastDurationSlider.Value,
                MaxWidth = (int)MaxWidthSlider.Value
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
        catch (Exception ex)
        {
            _logger.LogException(ex, "Preview settings failed");
            MessageBox.Show(
                $"Failed to show preview:\n{ex.Message}",
                "Preview Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _captureTimer?.Stop();
        _captureTimer = null;
        _previewOverlay?.Close();
        _previewOverlay = null;
        _previewToast?.Close();
        _previewToast = null;
    }

    private async Task LoadLogsAsync()
    {
        try
        {
            var entries = await Task.Run(() => _logger.GetRecentLogs(maxEntries: 5000).ToList());
            _allLogEntries = entries;
            LogsListView.ItemsSource = _allLogEntries;
            LogsEmptyText.Visibility = _allLogEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "LoadLogsAsync");
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
        LogsEmptyText.Visibility = Visibility.Visible;
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
                    || (entry.SourceFile?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false);
            };
        }

        view.Refresh();
        LogsEmptyText.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        if (!int.TryParse(rb.Tag?.ToString(), out var index)) return;
        ShowSection(index);
    }

    private void ShowSection(int index)
    {
        var sections = new[]
        {
            GeneralSection,
            ModesSection,
            ModelsSection,
            DictionarySection,
            SnippetsSection,
            AppearanceSection,
            LogsSection
        };

        if (index >= 0 && index < sections.Length)
        {
            var target = sections[index];
            if (target.Parent is System.Windows.Controls.Panel panel)
            {
                panel.Children.Remove(target);
            }
            TabContentHost.Content = target;
        }
    }

    private void OpenSandboxButton_Click(object sender, RoutedEventArgs e)
    {
        _dialogService.ShowModelTestingDialog(
            this,
            _configService,
            _modelCatalog,
            _modelManager,
            _transcriptionService,
            _audioRecorderService,
            _textFormatter,
            _logger,
            _ggufStore);

        // Reload the config after closing the Sandbox to prevent stale state from overriding Sandbox changes.
        _config = _configService.Load();

        // Update UI controls with potential new values saved from the Sandbox
        CleanEnabledCheckBox.IsChecked = _config.CleanEnabled;
        if (CleanPromptTextBox != null)
        {
            CleanPromptTextBox.Text = _config.CleanPrompt;
        }
        UpdateCleanPromptControlsState();

        ListFormattingEnabledCheckBox.IsChecked = _config.ListFormattingEnabled;
        SpokenPunctuationCheckBox.IsChecked = _config.SpokenPunctuationEnabled;

        RefreshModesList();

        _ = PopulateChatModelComboBoxAsync();
        _ = PopulateWhisperModelComboBoxAsync();
        _ = RefreshModelsDashboardAsync();
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

public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b) return b ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new System.NotImplementedException();
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
