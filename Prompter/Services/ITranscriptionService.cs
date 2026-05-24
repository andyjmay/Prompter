namespace Prompter.Services;

public interface ITranscriptionService
{
    Task<string> TranscribeAsync(string wavPath, string language, CancellationToken ct);
}
