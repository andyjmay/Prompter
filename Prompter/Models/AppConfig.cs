using System.Text.Json.Serialization;

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
    [JsonPropertyName("Anchor")]
    public OverlayAnchor Anchor { get; init; } = OverlayAnchor.TopCenter;
    [JsonPropertyName("OffsetX")]
    public int OffsetX { get; init; } = 0;
    [JsonPropertyName("OffsetY")]
    public int OffsetY { get; init; } = 0;
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; init; } = true;
    [JsonPropertyName("ShowAudioLevelMeter")]
    public bool ShowAudioLevelMeter { get; init; } = true;
}

public record OverlayStyleConfig
{
    [JsonPropertyName("Theme")]
    public OverlayTheme Theme { get; init; } = OverlayTheme.Dark;
    [JsonPropertyName("AccentColor")]
    public string? AccentColor { get; init; } = null;
    [JsonPropertyName("TextColor")]
    public string? TextColor { get; init; } = null;
    [JsonPropertyName("ProcessingAccentColor")]
    public string? ProcessingAccentColor { get; init; } = null;
    [JsonPropertyName("OverlayBackgroundColor")]
    public string? OverlayBackgroundColor { get; init; } = null;
    [JsonPropertyName("ToastBackgroundColor")]
    public string? ToastBackgroundColor { get; init; } = null;
    [JsonPropertyName("FontFamily")]
    public string FontFamily { get; init; } = "Segoe UI";
    [JsonPropertyName("OverlayFontSize")]
    public int OverlayFontSize { get; init; } = 18;
    [JsonPropertyName("ToastTitleFontSize")]
    public int ToastTitleFontSize { get; init; } = 14;
    [JsonPropertyName("ToastBodyFontSize")]
    public int ToastBodyFontSize { get; init; } = 12;
    [JsonPropertyName("ShowStatusText")]
    public bool ShowStatusText { get; init; } = true;
    [JsonPropertyName("ListeningLabel")]
    public string ListeningLabel { get; init; } = "Listening...";
    [JsonPropertyName("ProcessingLabel")]
    public string ProcessingLabel { get; init; } = "Processing…";
    [JsonPropertyName("BackgroundOpacity")]
    public double BackgroundOpacity { get; init; } = 0.8;
    [JsonPropertyName("ToastOpacity")]
    public double ToastOpacity { get; init; } = 1.0;
    [JsonPropertyName("CornerRadius")]
    public int CornerRadius { get; init; } = 16;
    [JsonPropertyName("Padding")]
    public int Padding { get; init; } = 16;
    [JsonPropertyName("ShadowEnabled")]
    public bool ShadowEnabled { get; init; } = false;
    [JsonPropertyName("PulseSpeed")]
    public OverlayPulseSpeed PulseSpeed { get; init; } = OverlayPulseSpeed.Normal;
}

public record PreviewToastSpecificConfig
{
    [JsonPropertyName("Placement")]
    public OverlayPlacementConfig Placement { get; init; } = new() { Anchor = OverlayAnchor.BottomRight, OffsetX = -16, OffsetY = -16 };
    [JsonPropertyName("DurationSeconds")]
    public int DurationSeconds { get; init; } = 3;
    [JsonPropertyName("MaxWidth")]
    public int MaxWidth { get; init; } = 500;
}

public record AppConfig
{
    [JsonPropertyName("Version")]
    public int Version { get; init; } = 12;
    [JsonPropertyName("HotkeyModifiers")]
    public string HotkeyModifiers { get; init; } = "Win+Ctrl";
    [JsonPropertyName("HotkeyKey")]
    public string HotkeyKey { get; init; } = "";
    [JsonPropertyName("DefaultModeId")]
    public string DefaultModeId { get; init; } = ModeDefaults.StandardId;
    [JsonPropertyName("Modes")]
    public List<ModeConfig> Modes { get; init; } = new(ModeDefaults.AllBuiltIns);
    [JsonPropertyName("CleanEnabled")]
    public bool CleanEnabled { get; init; } = false;
    [JsonPropertyName("CleanPrompt")]
    public string CleanPrompt { get; init; } = "Remove filler words such as um, uh, like, you know, I mean, sort of, and basically. Do not rephrase sentences. Preserve all substantive content.";
    [JsonPropertyName("ListFormattingEnabled")]
    public bool ListFormattingEnabled { get; init; } = false;
    [JsonPropertyName("ModelIdleTtlMinutes")]
    public int ModelIdleTtlMinutes { get; init; } = 5;
    [JsonPropertyName("AutoStartWithWindows")]
    public bool AutoStartWithWindows { get; init; } = false;
    [JsonPropertyName("AudioFeedbackEnabled")]
    public bool AudioFeedbackEnabled { get; init; } = false;
    [JsonPropertyName("NotificationsEnabled")]
    public bool NotificationsEnabled { get; init; } = true;
    [JsonPropertyName("NotifyOnOutputReady")]
    public bool NotifyOnOutputReady { get; init; } = true;
    [JsonPropertyName("SpokenPunctuationEnabled")]
    public bool SpokenPunctuationEnabled { get; init; } = false;
    [JsonPropertyName("DictionaryEntries")]
    public List<DictionaryEntry> DictionaryEntries { get; init; } = new();
    [JsonPropertyName("Snippets")]
    public List<Snippet> Snippets { get; init; } = new();
    [JsonPropertyName("Language")]
    public string Language { get; init; } = "en";
    [JsonPropertyName("ChatModelId")]
    public string ChatModelId { get; init; } = "phi-3.5-mini";
    [JsonPropertyName("WhisperModelId")]
    public string WhisperModelId { get; init; } = "whisper-tiny";
    [JsonPropertyName("UseCustomWhisper")]
    public bool UseCustomWhisper { get; init; } = false;
    [JsonPropertyName("CustomWhisperModelPath")]
    public string CustomWhisperModelPath { get; init; } = "";
    [JsonPropertyName("UseCustomChat")]
    public bool UseCustomChat { get; init; } = false;
    [JsonPropertyName("CustomChatModelPath")]
    public string CustomChatModelPath { get; init; } = "";
    [JsonPropertyName("HuggingFaceToken")]
    public string HuggingFaceToken { get; init; } = "";
    [JsonPropertyName("PasteThresholdCharacters")]
    public int PasteThresholdCharacters { get; init; } = 150;
    [JsonPropertyName("UseClipboardPaste")]
    public bool UseClipboardPaste { get; init; } = true;
    [JsonPropertyName("ProcessingTimeoutSeconds")]
    public int ProcessingTimeoutSeconds { get; init; } = 120;

    [JsonPropertyName("RecordingOverlay")]
    public OverlayPlacementConfig RecordingOverlay { get; init; } = new() { OffsetY = 40 };
    [JsonPropertyName("PreviewToast")]
    public PreviewToastSpecificConfig PreviewToast { get; init; } = new();
    [JsonPropertyName("OverlayStyle")]
    public OverlayStyleConfig OverlayStyle { get; init; } = new();
}
