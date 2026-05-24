using System.Runtime.InteropServices;
using System.Text;

namespace Prompter.Services;

public class InputInjectorService
{
    private readonly FileLogger _logger;

    public InputInjectorService(FileLogger logger)
    {
        _logger = logger;
    }

    public void TypeText(string text)
    {
        _logger.Log($"Injecting {text.Length} chars via SendInput.");
        const int chunkSize = 100; // max events per chunk (2 per char)
        int totalSent = 0;
        int cbSize = Marshal.SizeOf<INPUT>();

        // Build event list, keeping surrogate pairs together
        var events = new List<INPUT>();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            events.Add(MakeKeyDown(c));
            events.Add(MakeKeyUp(c));

            // If this is a high surrogate, the next char is the low surrogate.
            // We already include it in the next iteration, which is correct
            // because SendInput with KEYEVENTF_UNICODE sends raw UTF-16 code units.
        }

        // Send in batches
        for (int i = 0; i < events.Count; i += chunkSize)
        {
            int len = Math.Min(chunkSize, events.Count - i);
            var batch = events.GetRange(i, len).ToArray();
            uint sent = SendInput((uint)batch.Length, batch, cbSize);
            if (sent != batch.Length)
            {
                _logger.Log($"SendInput sent {sent}/{batch.Length} events. Last Win32 error: {Marshal.GetLastWin32Error()}");
            }
            totalSent += (int)sent;
        }

        if (totalSent != events.Count)
        {
            _logger.Log($"SendInput total mismatch: expected {events.Count} input events, sent {totalSent}.");
        }
    }

    public void SimulatePaste()
    {
        _logger.Log("Simulating Ctrl+V via SendInput.");
        int cbSize = Marshal.SizeOf<INPUT>();

        var events = new INPUT[]
        {
            // Ctrl KeyDown
            new() { type = 1, ki = new KEYBDINPUT { wVk = 0x11, dwFlags = 0 } },
            // V KeyDown
            new() { type = 1, ki = new KEYBDINPUT { wVk = 0x56, dwFlags = 0 } },
            // V KeyUp
            new() { type = 1, ki = new KEYBDINPUT { wVk = 0x56, dwFlags = 0x0002 /* KEYEVENTF_KEYUP */ } },
            // Ctrl KeyUp
            new() { type = 1, ki = new KEYBDINPUT { wVk = 0x11, dwFlags = 0x0002 /* KEYEVENTF_KEYUP */ } }
        };

        uint sent = SendInput((uint)events.Length, events, cbSize);
        if (sent != events.Length)
        {
            _logger.Log($"SimulatePaste sent {sent}/{events.Length} events. Last Win32 error: {Marshal.GetLastWin32Error()}");
        }
    }

    private static INPUT MakeKeyDown(char c) => new()
    {
        type = 1,
        ki = new KEYBDINPUT { wScan = c, dwFlags = 0x0004 /* KEYEVENTF_UNICODE */ }
    };

    private static INPUT MakeKeyUp(char c) => new()
    {
        type = 1,
        ki = new KEYBDINPUT { wScan = c, dwFlags = 0x0004 | 0x0002 /* KEYEVENTF_UNICODE | KEYEVENTF_KEYUP */ }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
