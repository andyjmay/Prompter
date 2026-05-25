using Microsoft.AI.Foundry.Local;
using Prompter.Models;

namespace Prompter.Services;

public interface IModelManager : IAsyncDisposable
{
    bool WhisperReady { get; }
    bool ChatReady { get; }
    string? LoadedChatModelAlias { get; }
    string? LoadedWhisperModelAlias { get; }

    Task InitializeAsync(int idleTtlMinutes);
    Task EnsureModelsLoadedAsync();
    Task EnsureChatModelLoadedAsync(string alias);
    Task UnloadChatModelAsync();
    Task UnloadWhisperModelAsync();
    Task UnloadModelAsync(string alias);
    Task DownloadModelAsync(string alias);
    Task<IChatClient> GetChatClientAsync();
    Task<OpenAIAudioClient> GetAudioClientAsync();
    event Action<string, float>? ModelDownloadProgress;
}
