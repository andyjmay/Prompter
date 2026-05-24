using Prompter.Models;

namespace Prompter.Services;

public interface ITextFormatter
{
    Task<string> CleanupAsync(string rawText, FormatMode mode, CancellationToken ct);
}
