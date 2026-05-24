namespace Prompter.Models;

public enum FormatMode
{
    Standard,
    Formal,
    Raw,
    Debug
}

public class AppConfig
{
    public string HotkeyModifiers { get; set; } = "Win+Ctrl";
    public string HotkeyKey { get; set; } = "";
    public FormatMode DefaultMode { get; set; } = FormatMode.Standard;
    public int ModelIdleTtlMinutes { get; set; } = 5;
    public bool AutoStartWithWindows { get; set; } = false;
    public bool AudioFeedbackEnabled { get; set; } = false;
    public bool NotificationsEnabled { get; set; } = true;
    public string Language { get; set; } = "en";
    public string ChatModelId { get; set; } = "phi-3.5-mini";
    public string WhisperModelId { get; set; } = "whisper-tiny";
    public int PasteThresholdCharacters { get; set; } = 150;
    public bool UseClipboardPaste { get; set; } = true;

    public string? CustomSystemPrompt { get; set; }
}
