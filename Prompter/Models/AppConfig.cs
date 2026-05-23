namespace Prompter.Models;

public enum FormatMode
{
    Standard,
    Formal,
    Raw
}

public class AppConfig
{
    public string HotkeyModifiers { get; set; } = "Win+Ctrl";
    public string HotkeyKey { get; set; } = "P";
    public FormatMode DefaultMode { get; set; } = FormatMode.Standard;
    public int ModelIdleTtlMinutes { get; set; } = 5;
    public bool AutoStartWithWindows { get; set; } = false;
    public bool AudioFeedbackEnabled { get; set; } = false;
    public string Language { get; set; } = "en";
}
