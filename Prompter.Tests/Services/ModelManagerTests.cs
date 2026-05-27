using System.Reflection;
using Prompter.Models;
using Prompter.Services;
using Prompter.Tests.Fakes;
using Xunit;

namespace Prompter.Tests.Services;

public class ModelManagerTests
{
    [Fact]
    public async Task CheckIdleAsync_DoesNotUnloadWhileInferenceActive()
    {
        var accessor = new FakeFoundryLocalManagerAccessor();
        var config = new FakeConfigService(new AppConfig());
        var logger = new FakeFileLogger();
        var whisperProvider = new WhisperNetTranscriptionProvider(config, logger);
        var manager = new ModelManager(accessor, config, logger, whisperProvider);

        // Inject a slow custom chat client
        var slowClient = new SlowChatClient();
        var customChatField = typeof(ModelManager).GetField("_customChatClient", BindingFlags.NonPublic | BindingFlags.Instance);
        customChatField!.SetValue(manager, slowClient);

        // Obtain the wrapped chat client and start an inference
        var client = await manager.GetChatClientAsync();
        var inferenceTask = client.CompleteAsync(new List<ChatMessage>(), 0.5f, CancellationToken.None);

        // Wait until the inference has started
        await slowClient.Started.Task;

        // Trigger idle check with TTL = 0 (should attempt unload)
        var checkIdleMethod = typeof(ModelManager).GetMethod("CheckIdleAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)checkIdleMethod!.Invoke(manager, new object[] { 0, CancellationToken.None })!;

        // Verify the custom chat client was NOT unloaded
        Assert.NotNull(customChatField.GetValue(manager));

        // Allow inference to finish
        slowClient.Finish();
        await inferenceTask;

        // Now trigger idle check again
        await (Task)checkIdleMethod!.Invoke(manager, new object[] { 0, CancellationToken.None })!;

        // Verify the custom chat client WAS unloaded this time
        Assert.Null(customChatField.GetValue(manager));
    }

    [Fact]
    public async Task EnsureModelsLoadedAsync_HonorsCancellationToken()
    {
        var accessor = new FakeFoundryLocalManagerAccessor();
        var config = new FakeConfigService(new AppConfig());
        var logger = new FakeFileLogger();
        var whisperProvider = new WhisperNetTranscriptionProvider(config, logger);
        var manager = new ModelManager(accessor, config, logger, whisperProvider);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.EnsureModelsLoadedAsync(ct: cts.Token));
        Assert.NotNull(ex);
    }

    private class SlowChatClient : IChatClient
    {
        private readonly TaskCompletionSource _completionTcs = new();
        public TaskCompletionSource Started { get; } = new();

        public async Task<string?> CompleteAsync(List<ChatMessage> messages, float temperature, CancellationToken ct)
        {
            Started.SetResult();
            await _completionTcs.Task;
            return "done";
        }

        public void Finish() => _completionTcs.SetResult();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
