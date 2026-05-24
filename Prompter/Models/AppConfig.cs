namespace Prompter.Models;

public enum FormatMode
{
    Standard,
    Formal,
    Raw,
    Debug
}

public record AppConfig
{
    public int Version { get; init; } = 1;
    public string HotkeyModifiers { get; init; } = "Win+Ctrl";
    public string HotkeyKey { get; init; } = "";
    public FormatMode DefaultMode { get; init; } = FormatMode.Standard;
    public int ModelIdleTtlMinutes { get; init; } = 5;
    public bool AutoStartWithWindows { get; init; } = false;
    public bool AudioFeedbackEnabled { get; init; } = false;
    public bool NotificationsEnabled { get; init; } = true;
    public string Language { get; init; } = "en";
    public string ChatModelId { get; init; } = "phi-3.5-mini";
    public string WhisperModelId { get; init; } = "whisper-tiny";
    public int PasteThresholdCharacters { get; init; } = 150;
    public bool UseClipboardPaste { get; init; } = true;
    public int ProcessingTimeoutSeconds { get; init; } = 120;
    public string? CustomSystemPrompt { get; init; }
}
