using System.Text.Json.Serialization;

namespace Prompter.Models;

public record DictionaryEntry
{
    [JsonPropertyName("Word")]
    public string Word { get; init; } = "";
    [JsonPropertyName("Aliases")]
    public List<string> Aliases { get; init; } = new();
}
