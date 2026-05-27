using Microsoft.AI.Foundry.Local;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeModelManager : IModelManager
{
    public bool WhisperReady { get; set; } = true;
    public bool ChatReady { get; set; } = true;
    public string? LoadedChatModelAlias { get; set; } = ModelCatalog.DefaultChatAlias;
    public string? LoadedWhisperModelAlias { get; set; } = "whisper-tiny";

    private readonly IChatClient? _chatClient;

    public FakeModelManager(IChatClient? chatClient = null)
    {
        _chatClient = chatClient;
    }

    public Task InitializeAsync(int idleTtlMinutes) => Task.CompletedTask;

    public CancellationToken? LastEnsureModelsToken { get; private set; }
    public Task EnsureModelsLoadedAsync(string? targetModeId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        LastEnsureModelsToken = ct;
        return Task.CompletedTask;
    }

    public Task EnsureChatModelLoadedAsync(string alias) => Task.CompletedTask;

    public Task UnloadChatModelAsync() => Task.CompletedTask;

    public Task UnloadWhisperModelAsync() => Task.CompletedTask;

    public Task UnloadModelAsync(string alias) => Task.CompletedTask;

    public Task DownloadModelAsync(string alias) => Task.CompletedTask;

    public Task<IChatClient> GetChatClientAsync()
    {
        if (_chatClient == null)
            throw new InvalidOperationException("No chat client configured on FakeModelManager");
        return Task.FromResult(_chatClient);
    }

    public Task<OpenAIAudioClient> GetAudioClientAsync()
        => throw new NotSupportedException("FakeModelManager does not support audio clients");

#pragma warning disable CS0067
    public event Action<string, float>? ModelDownloadProgress;
#pragma warning restore CS0067

    public async ValueTask DisposeAsync() { }
}
