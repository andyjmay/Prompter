using System.Reflection;
using System.Runtime.CompilerServices;
using Prompter.Models;
using Prompter.Services;
using Prompter.Tests.Fakes;
using Whisper.net;
using Xunit;

namespace Prompter.Tests.Services;

public class WhisperNetTranscriptionProviderTests
{
    [Fact]
    public async Task TranscribeAsync_HoldsLockDuringProcessing()
    {
        var config = new FakeConfigService(new AppConfig { CustomWhisperModelPath = "dummy.bin" });
        var logger = new FakeFileLogger();
        var provider = new TestableProvider(config, logger);

        // Inject an uninitialized WhisperFactory so _factory is non-null
        var dummyFactory = (WhisperFactory)RuntimeHelpers.GetUninitializedObject(typeof(WhisperFactory));
        provider._factory = dummyFactory;

        var transcriptionDelay = new TaskCompletionSource();
        provider.TranscriptionDelay = transcriptionDelay.Task;

        var transcribeTask = provider.TranscribeAsync("dummy.wav", "en", CancellationToken.None);

        // Give TranscribeAsync time to enter ExecuteTranscriptionAsync
        await Task.Delay(200);

        // Verify lock is held during transcription (prevents concurrent unload)
        var lockField = typeof(WhisperNetTranscriptionProvider).GetField("_lock", BindingFlags.NonPublic | BindingFlags.Instance);
        var sema = (SemaphoreSlim)lockField!.GetValue(provider)!;
        Assert.Equal(0, sema.CurrentCount);

        // Allow transcription to finish
        transcriptionDelay.SetResult();
        await transcribeTask;

        // Lock should be released now
        Assert.Equal(1, sema.CurrentCount);
    }

    private class TestableProvider : WhisperNetTranscriptionProvider
    {
        public TestableProvider(IConfigService configService, IFileLogger fileLogger)
            : base(configService, fileLogger)
        {
        }

        public Task? TranscriptionDelay { get; set; }

        protected override async Task<string> ExecuteTranscriptionAsync(string wavPath, string language, CancellationToken ct)
        {
            if (TranscriptionDelay != null)
            {
                await TranscriptionDelay;
            }
            return "test result";
        }
    }
}
