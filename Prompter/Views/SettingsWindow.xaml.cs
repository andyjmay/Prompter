using System.Linq;
using System.Windows;
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
    private AppConfig _config;
    private DispatcherTimer? _captureTimer;

    public SettingsWindow(ConfigService configService, ClipboardService clipboardService, StartupService startupService, FileLogger logger)
    {
        InitializeComponent();
        _configService = configService;
        _clipboardService = clipboardService;
        _startupService = startupService;
        _logger = logger;
        _config = configService.Load();

        HotkeyTextBox.Text = $"{_config.HotkeyModifiers} + {_config.HotkeyKey}";
        ModeComboBox.SelectedIndex = (int)_config.DefaultMode;
        TtlSlider.Value = _config.ModelIdleTtlMinutes;
        TtlValue.Text = _config.ModelIdleTtlMinutes.ToString();
        AutoStartCheckBox.IsChecked = _config.AutoStartWithWindows;
        AudioFeedbackCheckBox.IsChecked = _config.AudioFeedbackEnabled;

        TtlSlider.ValueChanged += (_, e) => TtlValue.Text = e.NewValue.ToString("F0");
    }

    private void CaptureHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        HotkeyTextBox.Text = "Listening... (press a key combo)";
        CaptureHotkeyButton.IsEnabled = false;

        var capturedMods = new HashSet<string>();
        Key? capturedKey = null;

        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        var endTime = DateTime.Now.AddSeconds(3);

        _captureTimer.Tick += (_, _) =>
        {
            if (DateTime.Now >= endTime && capturedKey == null)
            {
                _captureTimer.Stop();
                CaptureHotkeyButton.IsEnabled = true;
                HotkeyTextBox.Text = "No key captured";
                return;
            }

            // Poll modifiers
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) capturedMods.Add("Ctrl");
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) capturedMods.Add("Alt");
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) capturedMods.Add("Shift");
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) capturedMods.Add("Win");

            // Find a non-modifier key
            foreach (Key k in Enum.GetValues(typeof(Key)))
            {
                if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
                    continue;

                if (Keyboard.IsKeyDown(k))
                {
                    capturedKey = k;
                    break;
                }
            }

            if (capturedKey != null)
            {
                _captureTimer.Stop();
                CaptureHotkeyButton.IsEnabled = true;
                var modStr = string.Join("+", capturedMods);
                HotkeyTextBox.Text = $"{modStr} + {capturedKey}";
            }
        };
        _captureTimer.Start();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var parts = HotkeyTextBox.Text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !IsValidKey(parts[^1]))
        {
            MessageBox.Show(
                "Please capture a valid hotkey before saving.",
                "Invalid Hotkey",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _config.HotkeyKey = parts[^1];
        _config.HotkeyModifiers = string.Join("+", parts[..^1]);

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

        _startupService.SetEnabled(_config.AutoStartWithWindows);

        _ = _configService.SaveAsync(_config);
        DialogResult = true;
        Close();
    }

    private static bool IsValidKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
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

        // Win + system shortcuts
        if (hasWin && !hasCtrl && !hasAlt && !hasShift)
        {
            // These are core Windows shell shortcuts; RegisterHotKey may succeed but they won't fire reliably
            var winReserved = new[] { "L", "R", "E", "I", "X", "TAB", "A", "S", "Q", "P", "D", "M", "N", "U", "V", "SHIFT", "C", "Z", "J", "H", "K", "G", "T", "COMMA", "PERIOD", "NUMLOCK", "PAUSE", "BREAK", "UP", "DOWN", "LEFT", "RIGHT", "HOME", "END", "PGUP", "PGDN", "INSERT", "DELETE", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "PRINTSCREEN", "SNAPSHOT", "SNAP", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            if (winReserved.Contains(k)) return true;
        }

        // Ctrl + Alt + Del (Secure Attention Sequence) — impossible to intercept, but warn anyway
        if (hasCtrl && hasAlt && !hasShift && k == "DELETE") return true;
        if (hasCtrl && hasAlt && !hasShift && k == "DEL") return true;

        // Alt + Tab / Alt + Esc — window manager switching
        if (hasAlt && !hasCtrl && !hasShift && !hasWin)
        {
            var altReserved = new[] { "TAB", "ESC", "F4" };
            if (altReserved.Contains(k)) return true;
        }

        // Ctrl + Alt + Tab — task switcher
        if (hasCtrl && hasAlt && !hasShift && !hasWin && k == "TAB") return true;

        // PrintScreen — usually system-level
        if (!hasWin && !hasCtrl && !hasAlt && !hasShift && (k == "PRINTSCREEN" || k == "SNAPSHOT" || k == "SNAP")) return true;

        return false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
