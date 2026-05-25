namespace Prompter.Services;

public record PipelineResult(
    string FinalText,
    string RawText,
    string? FormattedText,
    string? MatchedSnippetTrigger,
    bool UsedFormattingFallback,
    string LoadedWhisperAlias,
    string? LoadedChatAlias);

public interface IPipelineProcessor
{
    Task<PipelineResult> ProcessAsync(string wavPath, string modeId, CancellationToken ct = default, IProgress<string>? progress = null);
}
