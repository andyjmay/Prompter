namespace Prompter.Services;

public interface ITextFormatter
{
    Task<string> CleanupAsync(string rawText, string modeId, CancellationToken ct);
}
