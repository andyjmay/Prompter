using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Prompter.Services;

public class HotkeyService : IDisposable
{
    private readonly FileLogger _logger;
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _isRecording;
    private CancellationTokenSource? _pollCts;
    private DateTime _recordingStartTime;

    // Parsed hotkey requirements
    private bool _needsWin;
    private bool _needsCtrl;
    private bool _needsAlt;
    private bool _needsShift;
    private uint _requiredKeyVk; // 0 when no non-modifier key is required

    // Track physically pressed keys from the hook itself
    private readonly HashSet<uint> _pressedVks = new();

    public event Action? RecordingStarted;
    public event Action? RecordingStopped;

    public HotkeyService(FileLogger logger)
    {
        _logger = logger;
    }

    public void Initialize(Window window, string modifiers, string key)
    {
        ParseCombination(modifiers, key);

        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        if (_hookId == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err, $"SetWindowsHookEx failed for {modifiers}+{key}. A low-level keyboard hook could not be installed.");
        }

        _logger.Log($"Low-level keyboard hook installed: {modifiers}+{key}");
    }

    private void ParseCombination(string modifiers, string key)
    {
        _needsWin = false;
        _needsCtrl = false;
        _needsAlt = false;
        _needsShift = false;
        _requiredKeyVk = 0;

        var parts = modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) _needsWin = true;
            else if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) _needsCtrl = true;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) _needsAlt = true;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) _needsShift = true;
        }

        if (!string.IsNullOrEmpty(key))
            _requiredKeyVk = ParseKey(key);
    }

    private static uint ParseKey(string key)
    {
        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
            return char.ToUpperInvariant(key[0]);
        if (Enum.TryParse<Key>(key, true, out var vk))
            return (uint)KeyInterop.VirtualKeyFromKey(vk);
        return 0;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbd.vkCode;
            bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isKeyUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;
            bool isInjected = (kbd.flags & LLKHF_INJECTED) != 0;

            if (isKeyDown && !isInjected)
            {
                _pressedVks.Add(vk);
            }
            else if (isKeyUp)
            {
                _pressedVks.Remove(vk);
            }

            if (!_isRecording && isKeyDown && !isInjected)
            {
                bool combo = IsCombinationPressedFromState();
                if (combo)
                {
                    _logger.Log($"Hotkey detected (vk={vk}, state=[{string.Join(",", _pressedVks)}]) — starting recording.");
                    _isRecording = true;
                    _recordingStartTime = DateTime.Now;
                    try
                    {
                        RecordingStarted?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogException(ex, "RecordingStarted handler threw");
                        _isRecording = false;
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }
                    StartReleasePolling();
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool IsCombinationPressedFromState()
    {
        if (_needsWin && !_pressedVks.Contains(0x5B) && !_pressedVks.Contains(0x5C)) return false;
        if (_needsCtrl && !_pressedVks.Contains(0x11) && !_pressedVks.Contains(0xA2) && !_pressedVks.Contains(0xA3)) return false;
        if (_needsAlt && !_pressedVks.Contains(0x12) && !_pressedVks.Contains(0xA4) && !_pressedVks.Contains(0xA5)) return false;
        if (_needsShift && !_pressedVks.Contains(0x10) && !_pressedVks.Contains(0xA0) && !_pressedVks.Contains(0xA1)) return false;
        if (_requiredKeyVk != 0 && !_pressedVks.Contains(_requiredKeyVk)) return false;
        return true;
    }

    private void StartReleasePolling()
    {
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            try
            {
                while (!_pollCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(50, _pollCts.Token);

                    bool allStillPressed = true;

                    if (_needsWin)
                    {
                        if ((GetAsyncKeyState(0x5B) & 0x8000) == 0 && (GetAsyncKeyState(0x5C) & 0x8000) == 0)
                            allStillPressed = false;
                    }
                    if (allStillPressed && _needsCtrl && (GetAsyncKeyState(0x11) & 0x8000) == 0) allStillPressed = false;
                    if (allStillPressed && _needsAlt && (GetAsyncKeyState(0x12) & 0x8000) == 0) allStillPressed = false;
                    if (allStillPressed && _needsShift && (GetAsyncKeyState(0x10) & 0x8000) == 0) allStillPressed = false;
                    if (allStillPressed && _requiredKeyVk != 0 && (GetAsyncKeyState((int)_requiredKeyVk) & 0x8000) == 0) allStillPressed = false;

                    if (!allStillPressed)
                    {
                        var elapsed = DateTime.Now - _recordingStartTime;
                        if (elapsed < TimeSpan.FromMilliseconds(300))
                        {
                            _logger.Log($"Keys released after {elapsed.TotalMilliseconds:F0} ms — below minimum hold (300 ms), keeping recording alive.");
                            continue;
                        }

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

    public void Unregister()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _isRecording = false;
        _pressedVks.Clear();
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _logger.Log("Low-level keyboard hook unregistered.");
    }

    public void Dispose()
    {
        Unregister();
        _proc = null;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x00000010;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
