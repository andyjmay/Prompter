using System.Net.Http;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Prompter.Models;
using Prompter.Services;
using Prompter.Tests.Fakes;
using Prompter.Tests.Helpers;
using Xunit;

namespace Prompter.Tests;

[Trait("Category", "Smoke")]
public class WhisperSmokeTests : IClassFixture<DispatcherFixture>, IAsyncDisposable
{
    private readonly DispatcherFixture _dispatcherFixture;
    private readonly ServiceProvider _provider;

    public WhisperSmokeTests(DispatcherFixture dispatcherFixture)
    {
        _dispatcherFixture = dispatcherFixture;
        var wavPath = TestAudioGenerator.EnsureAudioFile();
        var modelPath = EnsureWhisperModel();

        var config = new AppConfig
        {
            UseCustomWhisper = true,
            CustomWhisperModelPath = modelPath,
            UseClipboardPaste = false,
            PasteThresholdCharacters = 9999,
        };

        var services = new ServiceCollection();
        App.ConfigureServices(services);

        Replace<Dispatcher>(services, dispatcherFixture.Dispatcher);
        Replace<IAudioRecorderService>(services, new FakeAudioRecorderService(wavPath));
        Replace<IRecordingUIManager>(services, new FakeRecordingUIManager());
        Replace<IConfigService>(services, new FakeConfigService(config));
        Replace<IModelManager>(services, new FakeModelManager { ChatReady = false, WhisperReady = true });
        Replace<IInputInjectorService>(services, new CapturingInputInjectorService());
        Replace<IClipboardService>(services, new FakeClipboardService());
        Replace<IDialogService>(services, new FakeDialogService());
        Replace<IFileLogger>(services, new FakeFileLogger());

        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task RealWhisper_TranscribesTtsAudio()
    {
        var orchestrator = _provider.GetRequiredService<IPipelineOrchestrator>();
        var ui = _provider.GetRequiredService<IRecordingUIManager>() as FakeRecordingUIManager;
        var injector = _provider.GetRequiredService<IInputInjectorService>() as CapturingInputInjectorService;
        var logger = _provider.GetRequiredService<IFileLogger>() as FakeFileLogger;

        orchestrator.StartRecording();
        await Task.Delay(1100);
        orchestrator.StopRecordingAndProcess("standard");

        await ui!.CompletionTask.WaitAsync(TimeSpan.FromSeconds(30));

        if (injector!.Calls.Count == 0)
        {
            var logText = logger?.LogBuilder.ToString() ?? "(no logs)";
            Assert.Fail($"No injection calls were made. Log output:\n{logText}");
        }

        Assert.Single(injector.Calls);
        var text = injector.Calls[0].Text;

        Assert.Contains("hello", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("world", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test", text, StringComparison.OrdinalIgnoreCase);
    }

    internal static string EnsureWhisperModel()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prompter",
            "tests",
            "models");
        var path = System.IO.Path.Combine(dir, "ggml-tiny.bin");

        const long minSizeBytes = 30_000_000; // ~30 MB minimum for ggml-tiny

        if (System.IO.File.Exists(path))
        {
            var info = new System.IO.FileInfo(path);
            if (info.Length > 0 && info.Length >= minSizeBytes)
                return path;

            // File is corrupt/too small; delete and re-download
            try { System.IO.File.Delete(path); } catch { }
        }

        System.IO.Directory.CreateDirectory(dir);
        using var client = new HttpClient();
        const string url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin";
        var bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void EnsureWhisperModel_ValidatesFileSize()
    {
        var path = EnsureWhisperModel();
        var info = new FileInfo(path);
        Assert.True(info.Exists);
        Assert.True(info.Length > 0);
        Assert.True(info.Length >= 30_000_000);
    }

    private static void Replace<TService>(IServiceCollection services, TService instance) where TService : class
    {
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TService))
            {
                services.RemoveAt(i);
                break;
            }
        }
        services.AddSingleton<TService>(instance);
    }
}
