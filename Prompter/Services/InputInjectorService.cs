using System.Runtime.InteropServices;
using System.Text;

namespace Prompter.Services;

public class InputInjectorService : IInputInjectorService
{
    private readonly IFileLogger _logger;

    public InputInjectorService(IFileLogger logger)
    {
        _logger = logger;
    }

    public void TypeText(string text)
    {
        var sanitized = Sanitize(text);
        _logger.Log($"Injecting {sanitized.Length} chars via SendInput.");
        const int chunkSize = 100; // max events per chunk (2 per char)
        int totalSent = 0;
        int cbSize = Marshal.SizeOf<INPUT>();

        var events = new List<INPUT>();
        for (int i = 0; i < sanitized.Length; i++)
        {
            char c = sanitized[i];
            events.Add(MakeKeyDown(c));
            events.Add(MakeKeyUp(c));
        }

        for (int i = 0; i < events.Count;)
        {
            int len = Math.Min(chunkSize, events.Count - i);
            var batch = events.GetRange(i, len).ToArray();
            uint sent = SendInputRetry(batch, cbSize);
            if (sent != batch.Length)
            {
                _logger.Log($"SendInput sent {sent}/{batch.Length} events. Last Win32 error: {Marshal.GetLastWin32Error()}");
            }
            totalSent += (int)sent;
            i += (int)sent; // retry unsent events
        }

        if (totalSent != events.Count)
        {
            _logger.Log($"SendInput total mismatch: expected {events.Count} input events, sent {totalSent}.");
        }
    }

    private static uint SendInputRetry(INPUT[] inputs, int cbSize)
    {
        const int maxRetries = 3;
        int offset = 0;
        uint totalSent = 0;
        for (int attempt = 0; attempt < maxRetries && offset < inputs.Length; attempt++)
        {
            if (attempt > 0) Thread.Sleep(50);
            var slice = inputs[offset..];
            uint sent = SendInput((uint)slice.Length, slice, cbSize);
            totalSent += sent;
            offset += (int)sent;
            if (sent == (uint)slice.Length) break;
        }
        return totalSent;
    }

    private static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '\0') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public void SimulatePaste()
    {
        _logger.Log("Simulating Ctrl+V via SendInput.");
        int cbSize = Marshal.SizeOf<INPUT>();

        var events = new INPUT[]
        {
            new() { type = 1, ki = new KEYBDINPUT { wVk = 0x11, dwFlags = 0 } },
            new() { type = 1, ki = new KEYBDINPUT { wVk = 0x56, dwFlags = 0 } },
            new() { type = 1, ki = new KEYBDINPUT { wVk = 0x56, dwFlags = 0x0002 } },
            new() { type = 1, ki = new KEYBDINPUT { wVk = 0x11, dwFlags = 0x0002 } }
        };

        uint sent = SendInputRetry(events, cbSize);
        if (sent != events.Length)
        {
            _logger.Log($"SimulatePaste sent {sent}/{events.Length} events. Last Win32 error: {Marshal.GetLastWin32Error()}");
        }
    }

    private static INPUT MakeKeyDown(char c) => new()
    {
        type = 1,
        ki = new KEYBDINPUT { wScan = c, dwFlags = 0x0004 }
    };

    private static INPUT MakeKeyUp(char c) => new()
    {
        type = 1,
        ki = new KEYBDINPUT { wScan = c, dwFlags = 0x0004 | 0x0002 }
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
