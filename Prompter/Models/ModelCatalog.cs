using Prompter.Models;

namespace Prompter.Models;

public static class ModelCatalog
{
    public const string DefaultChatAlias = "phi-3.5-mini";
    public const string OtherOption = "Other…";

    public static readonly Dictionary<string, ModelMetadata> Metadata = new(StringComparer.OrdinalIgnoreCase)
    {
        ["whisper-tiny"] = new("~75 MB", "Speech Transcription", 75f),
        ["whisper-tiny-en"] = new("~75 MB", "Speech Transcription", 75f),
        ["whisper-base"] = new("~145 MB", "Speech Transcription", 145f),
        ["whisper-base-en"] = new("~145 MB", "Speech Transcription", 145f),
        ["whisper-small"] = new("~460 MB", "Speech Transcription", 460f),
        ["whisper-small-en"] = new("~460 MB", "Speech Transcription", 460f),
        ["phi-3.5-mini"] = new("~2.2 GB", "Text Correction", 2200f),
        ["phi-3.5-mini-instruct"] = new("~2.2 GB", "Text Correction", 2200f),
        ["qwen2.5-0.5b-instruct"] = new("~0.5 GB", "Text Correction", 500f),
        ["qwen2.5-1.5b-instruct"] = new("~0.9 GB", "Text Correction", 900f),
    };

    public static string GetSizeDescription(string alias)
    {
        if (Metadata.TryGetValue(alias, out var meta))
            return meta.SizeDescription;
        return "Unknown";
    }

    public static float? GetSizeInMegabytes(string alias)
    {
        if (Metadata.TryGetValue(alias, out var meta))
            return meta.SizeInMegabytes;
        return null;
    }

    public static string GetTaskType(string alias)
    {
        if (alias.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase))
            return "Speech Transcription";
        if (Metadata.TryGetValue(alias, out var meta))
            return meta.TaskType;
        return "Text Correction";
    }
}
