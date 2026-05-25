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

    public void SendKeys(string expansion)
    {
        ArgumentNullException.ThrowIfNull(expansion);
        var sanitized = Sanitize(expansion);
        var inputs = ParseExpansion(sanitized);
        _logger.Log($"Injecting {inputs.Count} input events via SendKeys.");

        int cbSize = Marshal.SizeOf<INPUT>();
        const int chunkSize = 100;
        int totalSent = 0;

        for (int i = 0; i < inputs.Count;)
        {
            int len = Math.Min(chunkSize, inputs.Count - i);
            var batch = inputs.GetRange(i, len).ToArray();
            uint sent = SendInputRetry(batch, cbSize);
            if (sent != batch.Length)
            {
                _logger.Log($"SendInput sent {sent}/{batch.Length} events. Last Win32 error: {Marshal.GetLastWin32Error()}");
            }
            totalSent += (int)sent;
            i += (int)sent;
        }

        if (totalSent != inputs.Count)
        {
            _logger.Log($"SendKeys total mismatch: expected {inputs.Count} input events, sent {totalSent}.");
        }
    }

    private static readonly HashSet<string> ValidTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enter", "Tab", "Escape", "Backspace", "Delete",
        "Up", "Down", "Left", "Right",
        "Home", "End", "PageUp", "PageDown",
        "Ctrl+A", "Ctrl+C", "Ctrl+V"
    };

    public void ValidateExpansion(string expansion)
    {
        ArgumentNullException.ThrowIfNull(expansion);
        int i = 0;
        while (i < expansion.Length)
        {
            char c = expansion[i];
            if (c == '{')
            {
                if (i + 1 < expansion.Length && expansion[i + 1] == '{')
                {
                    i += 2;
                    continue;
                }
                int close = expansion.IndexOf('}', i + 1);
                if (close == -1)
                    throw new ArgumentException("Unmatched opening brace '{'.");
                string token = expansion.Substring(i + 1, close - i - 1);
                if (!ValidTokens.Contains(token))
                    throw new ArgumentException($"Unknown key token '{{{token}}}'. Valid tokens are: {string.Join(", ", ValidTokens)}.");
                i = close + 1;
            }
            else if (c == '}')
            {
                if (i + 1 < expansion.Length && expansion[i + 1] == '}')
                {
                    i += 2;
                    continue;
                }
                throw new ArgumentException("Unmatched closing brace '}'.");
            }
            else
            {
                i++;
            }
        }
    }

    public bool ContainsKeyTokens(string expansion)
    {
        ArgumentNullException.ThrowIfNull(expansion);
        int i = 0;
        while (i < expansion.Length)
        {
            char c = expansion[i];
            if (c == '{')
            {
                if (i + 1 < expansion.Length && expansion[i + 1] == '{')
                {
                    i += 2;
                    continue;
                }
                int close = expansion.IndexOf('}', i + 1);
                if (close == -1) return false;
                string token = expansion.Substring(i + 1, close - i - 1);
                if (ValidTokens.Contains(token)) return true;
                i = close + 1;
            }
            else
            {
                i++;
            }
        }
        return false;
    }

    private static List<INPUT> ParseExpansion(string expansion)
    {
        var inputs = new List<INPUT>();
        int i = 0;
        while (i < expansion.Length)
        {
            char c = expansion[i];
            if (c == '{')
            {
                if (i + 1 < expansion.Length && expansion[i + 1] == '{')
                {
                    inputs.Add(MakeKeyDown('{'));
                    inputs.Add(MakeKeyUp('{'));
                    i += 2;
                    continue;
                }
                int close = expansion.IndexOf('}', i + 1);
                if (close == -1)
                    throw new ArgumentException($"Unmatched opening brace at position {i}.");
                string token = expansion.Substring(i + 1, close - i - 1);
                inputs.AddRange(TokenToInputs(token));
                i = close + 1;
            }
            else if (c == '}')
            {
                if (i + 1 < expansion.Length && expansion[i + 1] == '}')
                {
                    inputs.Add(MakeKeyDown('}'));
                    inputs.Add(MakeKeyUp('}'));
                    i += 2;
                    continue;
                }
                throw new ArgumentException($"Unmatched closing brace at position {i}.");
            }
            else
            {
                inputs.Add(MakeKeyDown(c));
                inputs.Add(MakeKeyUp(c));
                i++;
            }
        }
        return inputs;
    }

    private static IEnumerable<INPUT> TokenToInputs(string token)
    {
        if (!ValidTokens.Contains(token))
            throw new ArgumentException($"Unknown key token '{{{token}}}'.");

        string upper = token.ToUpperInvariant();
        return upper switch
        {
            "ENTER" => new[] { MakeVkKeyDown(0x0D), MakeVkKeyUp(0x0D) },
            "TAB" => new[] { MakeVkKeyDown(0x09), MakeVkKeyUp(0x09) },
            "ESCAPE" => new[] { MakeVkKeyDown(0x1B), MakeVkKeyUp(0x1B) },
            "BACKSPACE" => new[] { MakeVkKeyDown(0x08), MakeVkKeyUp(0x08) },
            "DELETE" => new[] { MakeVkKeyDown(0x2E), MakeVkKeyUp(0x2E) },
            "UP" => new[] { MakeVkKeyDown(0x26, extended: true), MakeVkKeyUp(0x26, extended: true) },
            "DOWN" => new[] { MakeVkKeyDown(0x28, extended: true), MakeVkKeyUp(0x28, extended: true) },
            "LEFT" => new[] { MakeVkKeyDown(0x25, extended: true), MakeVkKeyUp(0x25, extended: true) },
            "RIGHT" => new[] { MakeVkKeyDown(0x27, extended: true), MakeVkKeyUp(0x27, extended: true) },
            "HOME" => new[] { MakeVkKeyDown(0x24, extended: true), MakeVkKeyUp(0x24, extended: true) },
            "END" => new[] { MakeVkKeyDown(0x23, extended: true), MakeVkKeyUp(0x23, extended: true) },
            "PAGEUP" => new[] { MakeVkKeyDown(0x21, extended: true), MakeVkKeyUp(0x21, extended: true) },
            "PAGEDOWN" => new[] { MakeVkKeyDown(0x22, extended: true), MakeVkKeyUp(0x22, extended: true) },
            "CTRL+A" => new[] { MakeVkKeyDown(0x11), MakeVkKeyDown(0x41), MakeVkKeyUp(0x41), MakeVkKeyUp(0x11) },
            "CTRL+C" => new[] { MakeVkKeyDown(0x11), MakeVkKeyDown(0x43), MakeVkKeyUp(0x43), MakeVkKeyUp(0x11) },
            "CTRL+V" => new[] { MakeVkKeyDown(0x11), MakeVkKeyDown(0x56), MakeVkKeyUp(0x56), MakeVkKeyUp(0x11) },
            _ => throw new ArgumentException($"Unknown key token '{{{token}}}'.")
        };
    }

    private static INPUT MakeVkKeyDown(ushort vk, bool extended = false) => new()
    {
        type = 1,
        ki = new KEYBDINPUT { wVk = vk, dwFlags = extended ? 0x0001u : 0u }
    };

    private static INPUT MakeVkKeyUp(ushort vk, bool extended = false) => new()
    {
        type = 1,
        ki = new KEYBDINPUT { wVk = vk, dwFlags = (extended ? 0x0001u : 0u) | 0x0002u }
    };

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
