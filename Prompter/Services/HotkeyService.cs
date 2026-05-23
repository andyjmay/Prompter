using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Prompter.Services;

public class HotkeyService : IDisposable
{
    private readonly FileLogger _logger;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private int _hotkeyId;
    private bool _isRecording;
    private CancellationTokenSource? _pollCts;

    private uint _registeredVk;
    private List<int> _requiredVks = new();
    private bool _needsWin;

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;

    public HotkeyService(FileLogger logger)
    {
        _logger = logger;
        _hotkeyId = new Random().Next(1000, 60000); // avoid collision with other apps
    }

    public void Initialize(Window window, string modifiers, string key)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        uint fsModifiers = ParseModifiers(modifiers);
        uint vk = ParseKey(key);
        _registeredVk = vk;

        _requiredVks.Clear();
        _needsWin = false;
        if ((fsModifiers & 0x0008) != 0) _needsWin = true; // Win (checked with OR logic later)
        if ((fsModifiers & 0x0002) != 0) _requiredVks.Add(0x11); // Ctrl (generic, covers L+R)
        if ((fsModifiers & 0x0001) != 0) _requiredVks.Add(0x12); // Alt (generic, covers L+R)
        if ((fsModifiers & 0x0004) != 0) _requiredVks.Add(0x10); // Shift (generic, covers L+R)
        _requiredVks.Add((int)vk);

        try
        {
            if (!RegisterHotKey(_hwnd, _hotkeyId, fsModifiers, vk))
            {
                int err = Marshal.GetLastWin32Error();
                _source?.RemoveHook(WndProc);
                _logger.Log($"Failed to register global hotkey (error {err}).");
                throw new Win32Exception(err, $"RegisterHotKey failed for {modifiers}+{key}. The hotkey may already be in use by another application.");
            }
        }
        catch
        {
            _source?.RemoveHook(WndProc);
            throw;
        }

        _logger.Log($"Hotkey registered: {modifiers}+{key} (id={_hotkeyId})");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            if (!_isRecording)
            {
                _isRecording = true;
                _logger.Log("Hotkey pressed — starting recording.");
                RecordingStarted?.Invoke();
                StartReleasePolling();
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void StartReleasePolling()
    {
        _pollCts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            try
            {
                while (!_pollCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(50, _pollCts.Token);

                    // If ANY required key is released, stop recording
                    bool allStillPressed = true;

                    // Win must be checked with OR (LWin 0x5B OR RWin 0x5C), because they are distinct VKs
                    if (_needsWin)
                    {
                        if ((GetAsyncKeyState(0x5B) & 0x8000) == 0 && (GetAsyncKeyState(0x5C) & 0x8000) == 0)
                        {
                            allStillPressed = false;
                        }
                    }

                    foreach (var vk in _requiredVks)
                    {
                        if (!allStillPressed) break;
                        if ((GetAsyncKeyState(vk) & 0x8000) == 0)
                        {
                            allStillPressed = false;
                            break;
                        }
                    }

                    if (!allStillPressed)
                    {
                        _isRecording = false;
                        _pollCts.Cancel();
                        _logger.Log("Hotkey released — stopping recording.");
                        RecordingStopped?.Invoke();
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }, _pollCts.Token);
    }

    private static uint ParseModifiers(string text)
    {
        uint mod = 0;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mod |= 0x0002; // MOD_CONTROL
            if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mod |= 0x0001; // MOD_ALT
            if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mod |= 0x0004; // MOD_SHIFT
            if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) mod |= 0x0008; // MOD_WIN
        }
        return mod;
    }

    private static uint ParseKey(string key)
    {
        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            return char.ToUpperInvariant(key[0]);
        }
        if (Enum.TryParse<Key>(key, true, out var vk))
        {
            return (uint)KeyInterop.VirtualKeyFromKey(vk);
        }
        return 0x50; // default P
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _isRecording = false;
        if (_hwnd != IntPtr.Zero)
            UnregisterHotKey(_hwnd, _hotkeyId);
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
