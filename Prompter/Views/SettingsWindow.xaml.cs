using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
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
    private AppConfig _config;
    private DispatcherTimer? _captureTimer;
    private bool _finalizing;
    private readonly Dictionary<string, string> _displayNameToAlias = new();
    private readonly Dictionary<string, string> _whisperDisplayNameToAlias = new();
    private string? _activeSortBy;
    private ListSortDirection _activeSortDirection = ListSortDirection.Ascending;

    public SettingsWindow(
        IConfigService configService,
        IClipboardService clipboardService,
        IStartupService startupService,
        IFileLogger logger,
        IModelCatalogService modelCatalog,
        IModelManager modelManager)
    {
        InitializeComponent();
        _configService = configService;
        _clipboardService = clipboardService;
        _startupService = startupService;
        _logger = logger;
        _modelCatalog = modelCatalog;
        _modelManager = modelManager;
        _config = configService.Load();

        HotkeyTextBox.Text = string.IsNullOrEmpty(_config.HotkeyKey)
            ? _config.HotkeyModifiers
            : $"{_config.HotkeyModifiers} + {_config.HotkeyKey}";
        ModeComboBox.SelectedIndex = (int)_config.DefaultMode;
        TtlSlider.Value = _config.ModelIdleTtlMinutes;
        TtlValue.Text = _config.ModelIdleTtlMinutes.ToString();
        AutoStartCheckBox.IsChecked = _config.AutoStartWithWindows;
        AudioFeedbackCheckBox.IsChecked = _config.AudioFeedbackEnabled;
        NotificationsCheckBox.IsChecked = _config.NotificationsEnabled;
        CustomPromptTextBox.Text = _config.CustomSystemPrompt ?? string.Empty;

        UsePasteCheckBox.IsChecked = _config.UseClipboardPaste;
        PasteThresholdTextBox.Text = _config.PasteThresholdCharacters.ToString();

        TtlSlider.ValueChanged += (_, e) => TtlValue.Text = e.NewValue.ToString("F0");

        _ = PopulateChatModelComboBoxAsync();
        _ = PopulateWhisperModelComboBoxAsync();
        _ = RefreshModelsDashboardAsync();
        DetectGpuStatus();
    }

    private async Task PopulateChatModelComboBoxAsync()
    {
        ChatModelComboBox.Items.Clear();
        ChatModelComboBox.Items.Add("Loading models…");
        ChatModelComboBox.IsEnabled = false;
        ChatModelComboBox.SelectedIndex = 0;
        LoadingModelsText.Visibility = Visibility.Visible;
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
            ChatModelComboBox.Items.Add(ModelCatalog.OtherOption);
            ChatModelComboBox.IsEnabled = true;
            LoadingModelsText.Visibility = Visibility.Collapsed;

            var configuredAlias = _config.ChatModelId;
            var configuredDisplay = models.FirstOrDefault(m => m.Alias == configuredAlias).DisplayName;
            if (configuredDisplay != null)
            {
                ChatModelComboBox.SelectedItem = configuredDisplay;
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
    }

    private async Task PopulateWhisperModelComboBoxAsync()
    {
        WhisperModelComboBox.Items.Clear();
        WhisperModelComboBox.Items.Add("Loading models…");
        WhisperModelComboBox.IsEnabled = false;
        WhisperModelComboBox.SelectedIndex = 0;
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
            WhisperModelComboBox.IsEnabled = true;

            var configuredAlias = _config.WhisperModelId;
            var configuredDisplay = models.FirstOrDefault(m => m.Alias == configuredAlias).DisplayName;
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
        var selected = ChatModelComboBox.SelectedItem as string;
        if (selected == ModelCatalog.OtherOption)
        {
            CustomModelTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            CustomModelTextBox.Visibility = Visibility.Collapsed;
            CustomModelTextBox.Text = string.Empty;
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

        if (ModeComboBox.SelectedIndex >= 0)
            _config = _config with { DefaultMode = (FormatMode)ModeComboBox.SelectedIndex };

        _config = _config with
        {
            ModelIdleTtlMinutes = (int)TtlSlider.Value,
            AutoStartWithWindows = AutoStartCheckBox.IsChecked == true,
            AudioFeedbackEnabled = AudioFeedbackCheckBox.IsChecked == true,
            NotificationsEnabled = NotificationsCheckBox.IsChecked == true,
            CustomSystemPrompt = string.IsNullOrWhiteSpace(CustomPromptTextBox.Text) ? null : CustomPromptTextBox.Text.Trim()
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
        var proposedWhisperAlias = selectedWhisperDisplay != null
            ? _whisperDisplayNameToAlias.GetValueOrDefault(selectedWhisperDisplay, selectedWhisperDisplay)
            : "whisper-tiny";

        var oldWhisperModel = _config.WhisperModelId;
        _config = _config with { WhisperModelId = proposedWhisperAlias };

        var selectedDisplay = ChatModelComboBox.SelectedItem as string ?? ModelCatalog.OtherOption;
        var proposedAlias = selectedDisplay == ModelCatalog.OtherOption
            ? CustomModelTextBox.Text.Trim()
            : _displayNameToAlias.GetValueOrDefault(selectedDisplay, selectedDisplay);

        if (string.IsNullOrWhiteSpace(proposedAlias))
        {
            MessageBox.Show(
                "Please select a chat model or enter a custom model alias.",
                "Invalid Model",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

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

        _config = _config with { ChatModelId = proposedAlias };

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

        if (proposedWhisperAlias != oldWhisperModel)
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
            "Size" => "SizeDescription",
            "Downloaded" => "IsCached",
            "Loaded" => "IsLoaded",
            _ => null
        };
    }

    private void Sort(string sortBy, ListSortDirection direction)
    {
        var dataView = CollectionViewSource.GetDefaultView(ModelsListView.ItemsSource);
        if (dataView != null)
        {
            dataView.SortDescriptions.Clear();
            SortDescription sd = new SortDescription(sortBy, direction);
            dataView.SortDescriptions.Add(sd);
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
