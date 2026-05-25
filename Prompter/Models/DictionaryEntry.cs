namespace Prompter.Models;

public record DictionaryEntry
{
    public string Word { get; init; } = "";
    public List<string> Aliases { get; init; } = new();
}
