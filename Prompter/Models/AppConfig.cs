namespace Prompter.Models;

public enum OverlayTheme
{
    Dark,
    Light,
    HighContrast,
    Minimal
}

public enum OverlayAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public enum OverlayPulseSpeed
{
    Slow,
    Normal,
    Fast
}

public record OverlayPlacementConfig
{
    public OverlayAnchor Anchor { get; init; } = OverlayAnchor.TopCenter;
    public int OffsetX { get; init; } = 0;
    public int OffsetY { get; init; } = 0;
    public bool Enabled { get; init; } = true;
    public bool ShowAudioLevelMeter { get; init; } = true;
}

public record OverlayStyleConfig
{
    public OverlayTheme Theme { get; init; } = OverlayTheme.Dark;
    public string? AccentColor { get; init; } = null;
    public string? TextColor { get; init; } = null;
    public string? ProcessingAccentColor { get; init; } = null;
    public string? OverlayBackgroundColor { get; init; } = null;
    public string? ToastBackgroundColor { get; init; } = null;
    public string FontFamily { get; init; } = "Segoe UI";
    public int OverlayFontSize { get; init; } = 18;
    public int ToastTitleFontSize { get; init; } = 14;
    public int ToastBodyFontSize { get; init; } = 12;
    public bool ShowStatusText { get; init; } = true;
    public string ListeningLabel { get; init; } = "Listening...";
    public string ProcessingLabel { get; init; } = "Processing…";
    public double BackgroundOpacity { get; init; } = 0.8;
    public double ToastOpacity { get; init; } = 1.0;
    public int CornerRadius { get; init; } = 16;
    public int Padding { get; init; } = 16;
    public bool ShadowEnabled { get; init; } = false;
    public OverlayPulseSpeed PulseSpeed { get; init; } = OverlayPulseSpeed.Normal;
}

public record PreviewToastSpecificConfig
{
    public OverlayPlacementConfig Placement { get; init; } = new() { Anchor = OverlayAnchor.BottomRight, OffsetX = -16, OffsetY = -16 };
    public int DurationSeconds { get; init; } = 3;
    public int MaxWidth { get; init; } = 500;
}

public record AppConfig
{
    public int Version { get; init; } = 12;
    public string HotkeyModifiers { get; init; } = "Win+Ctrl";
    public string HotkeyKey { get; init; } = "";
    public string DefaultModeId { get; init; } = ModeDefaults.StandardId;
    public List<ModeConfig> Modes { get; init; } = new(ModeDefaults.AllBuiltIns);
    public bool CleanEnabled { get; init; } = false;
    public string CleanPrompt { get; init; } = "Remove filler words such as um, uh, like, you know, I mean, sort of, and basically. Do not rephrase sentences. Preserve all substantive content.";
    public bool ListFormattingEnabled { get; init; } = false;
    public int ModelIdleTtlMinutes { get; init; } = 5;
    public bool AutoStartWithWindows { get; init; } = false;
    public bool AudioFeedbackEnabled { get; init; } = false;
    public bool NotificationsEnabled { get; init; } = true;
    public bool NotifyOnOutputReady { get; init; } = true;
    public bool SpokenPunctuationEnabled { get; init; } = false;
    public List<DictionaryEntry> DictionaryEntries { get; init; } = new();
    public List<Snippet> Snippets { get; init; } = new();
    public string Language { get; init; } = "en";
    public string ChatModelId { get; init; } = "phi-3.5-mini";
    public string WhisperModelId { get; init; } = "whisper-tiny";
    public bool UseCustomWhisper { get; init; } = false;
    public string CustomWhisperModelPath { get; init; } = "";
    public bool UseCustomChat { get; init; } = false;
    public string CustomChatModelPath { get; init; } = "";
    public string HuggingFaceToken { get; init; } = "";
    public int PasteThresholdCharacters { get; init; } = 150;
    public bool UseClipboardPaste { get; init; } = true;
    public int ProcessingTimeoutSeconds { get; init; } = 120;

    public OverlayPlacementConfig RecordingOverlay { get; init; } = new() { OffsetY = 40 };
    public PreviewToastSpecificConfig PreviewToast { get; init; } = new();
    public OverlayStyleConfig OverlayStyle { get; init; } = new();
}
