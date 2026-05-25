namespace Prompter.Models;

public record Snippet
{
    public string Trigger { get; init; } = "";
    public string Expansion { get; init; } = "";
}
