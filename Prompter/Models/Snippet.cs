using System.Text.Json.Serialization;

namespace Prompter.Models;

public record Snippet
{
    [JsonPropertyName("Trigger")]
    public string Trigger { get; init; } = "";
    [JsonPropertyName("Expansion")]
    public string Expansion { get; init; } = "";
}
