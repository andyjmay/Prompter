using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeTranscriptionService : ITranscriptionService
{
    public string FixedResult { get; set; } = "";

    public Task<string> TranscribeAsync(string wavPath, string language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(FixedResult);
    }
}
