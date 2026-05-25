namespace Prompter.Models;

public record LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Message { get; init; } = "";
    public bool IsError { get; init; }
    public string SourceFile { get; init; } = "";
}
