namespace Prompter.Models;

public record ModeConfig
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string SystemPrompt { get; init; } = "";
    public bool SkipFormatting { get; init; } = false;
    public bool ShowDiagnosticOutput { get; init; } = false;
    public bool IsBuiltIn { get; init; } = false;
}
