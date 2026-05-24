using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
    private readonly ConfigService _configService;
    private readonly ClipboardService _clipboardService;
    private readonly StartupService _startupService;
    private readonly FileLogger _logger;
    private readonly FoundryOrchestrator _foundry;
    private AppConfig _config;
    private DispatcherTimer? _captureTimer;
    private bool _finalizing;
    private readonly Dictionary<string, string> _displayNameToAlias = new();
    private readonly Dictionary<string, string> _whisperDisplayNameToAlias = new();
    private string? _activeSortBy;
    private ListSortDirection _activeSortDirection = ListSortDirection.Ascending;

    public SettingsWindow(ConfigService configService, ClipboardService clipboardService, StartupService startupService, FileLogger logger, FoundryOrchestrator foundry)
    {
        InitializeComponent();
        _configService = configService;
        _clipboardService = clipboardService;
        _startupService = startupService;
        _logger = logger;
        _foundry = foundry;
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

        // New options fields
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

        // Wait for Foundry Local to finish initializing (up to ~15 seconds)
        int attempts = 0;
        while (!_foundry.IsInitialized && attempts < 30)
        {
            await Task.Delay(500);
            attempts++;
        }

        if (!_foundry.IsInitialized)
        {
            _logger.Log("Foundry Local not initialized after waiting. Showing limited model options.");
            ChatModelComboBox.Items.Clear();
            ChatModelComboBox.Items.Add(ModelCatalog.OtherOption);
            ChatModelComboBox.IsEnabled = true;
            LoadingModelsText.Visibility = Visibility.Collapsed;
            ChatModelComboBox.SelectedItem = ModelCatalog.OtherOption;
            return;
        }

        try
        {
            var models = await _foundry.ListAvailableChatModelsAsync();

            ChatModelComboBox.Items.Clear();
            foreach (var (alias, displayName) in models)
            {
                _displayNameToAlias[displayName] = alias;
                ChatModelComboBox.Items.Add(displayName);
            }
            ChatModelComboBox.Items.Add(ModelCatalog.OtherOption);
            ChatModelComboBox.IsEnabled = true;
            LoadingModelsText.Visibility = Visibility.Collapsed;

            // Try to select the currently configured model
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

        int attempts = 0;
        while (!_foundry.IsInitialized && attempts < 30)
        {
            await Task.Delay(500);
            attempts++;
        }

        if (!_foundry.IsInitialized)
        {
            WhisperModelComboBox.Items.Clear();
            WhisperModelComboBox.Items.Add("whisper-tiny");
            _whisperDisplayNameToAlias["whisper-tiny"] = "whisper-tiny";
            WhisperModelComboBox.SelectedIndex = 0;
            WhisperModelComboBox.IsEnabled = true;
            return;
        }

        try
        {
            var models = await _foundry.ListAvailableWhisperModelsAsync();

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
            int attempts = 0;
            while (!_foundry.IsInitialized && attempts < 30)
            {
                await Task.Delay(500);
                attempts++;
            }

            if (!_foundry.IsInitialized) return;

            var statusList = await _foundry.GetModelStatusListAsync();
            ModelsListView.ItemsSource = statusList;

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
            await Task.Run(() => _foundry.DownloadModelAsync(selected.Alias));
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
            await _foundry.UnloadModelAsync(selected.Alias);
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
                // No regular key pressed — check for stable modifiers (modifier-only hotkey)
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
            _config.HotkeyModifiers = string.Join("+", parts);
            _config.HotkeyKey = "";
        }
        else
        {
            _config.HotkeyKey = parts[^1];
            _config.HotkeyModifiers = string.Join("+", parts[..^1]);
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
            _config.DefaultMode = (FormatMode)ModeComboBox.SelectedIndex;

        _config.ModelIdleTtlMinutes = (int)TtlSlider.Value;
        _config.AutoStartWithWindows = AutoStartCheckBox.IsChecked == true;
        _config.AudioFeedbackEnabled = AudioFeedbackCheckBox.IsChecked == true;
        _config.NotificationsEnabled = NotificationsCheckBox.IsChecked == true;
        _config.CustomSystemPrompt = string.IsNullOrWhiteSpace(CustomPromptTextBox.Text) ? null : CustomPromptTextBox.Text.Trim();

        // Clipboard Paste
        _config.UseClipboardPaste = UsePasteCheckBox.IsChecked == true;
        if (int.TryParse(PasteThresholdTextBox.Text, out var threshold))
        {
            _config.PasteThresholdCharacters = threshold;
        }
        else
        {
            _config.PasteThresholdCharacters = 150;
        }

        // Whisper model selection
        var selectedWhisperDisplay = WhisperModelComboBox.SelectedItem as string;
        var proposedWhisperAlias = selectedWhisperDisplay != null
            ? _whisperDisplayNameToAlias.GetValueOrDefault(selectedWhisperDisplay, selectedWhisperDisplay)
            : "whisper-tiny";

        var oldWhisperModel = _config.WhisperModelId;
        _config.WhisperModelId = proposedWhisperAlias;

        // Chat model selection
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

        // Validate model alias against catalog
        bool isValid = await _foundry.IsModelInCatalogAsync(proposedAlias);
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

        _config.ChatModelId = proposedAlias;

        _startupService.SetEnabled(_config.AutoStartWithWindows);

        await _configService.SaveAsync(_config);

        // Unload chat model so the next pipeline run loads the newly selected one
        try
        {
            await _foundry.UnloadChatModelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "UnloadChatModelAsync on settings save");
        }

        // Unload whisper model if changed so the next pipeline run loads the newly selected one
        if (proposedWhisperAlias != oldWhisperModel)
        {
            try
            {
                await _foundry.UnloadWhisperModelAsync();
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "UnloadWhisperModelAsync on settings save");
            }
        }

        DialogResult = true;
        Close();
    }

    private static bool IsValidKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return true; // modifier-only is valid
        if (key.Length == 1 && char.IsLetterOrDigit(key[0])) return true;
        return Enum.TryParse<Key>(key, true, out _);
    }

    private static bool IsDangerousShortcut(string modifiers, string key)
    {
        var mods = modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasWin = mods.Any(m => m.Equals("Win", StringComparison.OrdinalIgnoreCase));
        var hasCtrl = mods.Any(m => m.Equals("Ctrl", StringComparison.OrdinalIgnoreCase));
        var hasAlt = mods.Any(m => m.Equals("Alt", StringComparison.OrdinalIgnoreCase));
        var hasShift = mods.Any(m => m.Equals("Shift", StringComparison.OrdinalIgnoreCase));

        var k = key.ToUpperInvariant();

        // Block bare Win key (opens Start menu)
        if (hasWin && !hasCtrl && !hasAlt && !hasShift && string.IsNullOrEmpty(k)) return true;

        // Win + key shortcuts — core Windows shell shortcuts
        if (hasWin && !hasCtrl && !hasAlt && !hasShift && !string.IsNullOrEmpty(k))
        {
            var winReserved = new[] { "L", "R", "E", "I", "X", "TAB", "A", "S", "Q", "P", "D", "M", "N", "U", "V", "SHIFT", "C", "Z", "J", "H", "K", "G", "T", "COMMA", "PERIOD", "NUMLOCK", "PAUSE", "BREAK", "UP", "DOWN", "LEFT", "RIGHT", "HOME", "END", "PGUP", "PGDN", "INSERT", "DELETE", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "PRINTSCREEN", "SNAPSHOT", "SNAP", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            if (winReserved.Contains(k)) return true;
        }

        // Ctrl + Alt + Del (Secure Attention Sequence)
        if (hasCtrl && hasAlt && !hasShift && (k == "DELETE" || k == "DEL")) return true;

        // Alt + Tab / Alt + Esc / Alt + F4
        if (hasAlt && !hasCtrl && !hasShift && !hasWin && !string.IsNullOrEmpty(k))
        {
            var altReserved = new[] { "TAB", "ESC", "F4" };
            if (altReserved.Contains(k)) return true;
        }

        // Ctrl + Alt + Tab
        if (hasCtrl && hasAlt && !hasShift && !hasWin && k == "TAB") return true;

        // PrintScreen alone
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
                    // Clean previous symbols
                    headerText = headerText.Replace(" ▲", "").Replace(" ▼", "");
                    
                    // If this is the active sorted column, add symbol
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
