using System.Text.Json.Serialization;

namespace Prompter.Models;

public record ModeConfig
{
    [JsonPropertyName("Id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("SystemPrompt")]
    public string SystemPrompt { get; init; } = "";
    [JsonPropertyName("SkipFormatting")]
    public bool SkipFormatting { get; init; } = false;
    [JsonPropertyName("ShowDiagnosticOutput")]
    public bool ShowDiagnosticOutput { get; init; } = false;
    [JsonPropertyName("IsBuiltIn")]
    public bool IsBuiltIn { get; init; } = false;
}
