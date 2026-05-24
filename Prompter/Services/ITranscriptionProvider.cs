using System.Threading;
using System.Threading.Tasks;

namespace Prompter.Services;

public interface ITranscriptionProvider
{
    bool IsLoaded { get; }
    Task LoadAsync(CancellationToken ct = default);
    Task UnloadAsync();
    Task<string> TranscribeAsync(string wavPath, string language, CancellationToken ct);
}
