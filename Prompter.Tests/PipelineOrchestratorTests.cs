using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Prompter.Models;
using Prompter.Services;
using Prompter.Tests.Fakes;
using Prompter.Tests.Helpers;
using Xunit;

namespace Prompter.Tests;

public class PipelineOrchestratorTests : IClassFixture<DispatcherFixture>, IAsyncDisposable
{
    private readonly DispatcherFixture _dispatcherFixture;
    private readonly ServiceProvider _provider;

    public PipelineOrchestratorTests(DispatcherFixture dispatcherFixture)
    {
        _dispatcherFixture = dispatcherFixture;
        var wavPath = TestAudioGenerator.EnsureAudioFile();

        var services = new ServiceCollection();
        App.ConfigureServices(services);

        Replace<Dispatcher>(services, dispatcherFixture.Dispatcher);
        Replace<IAudioRecorderService>(services, new FakeAudioRecorderService(wavPath));
        Replace<IRecordingUIManager>(services, new FakeRecordingUIManager());
        Replace<IConfigService>(services, new FakeConfigService(new AppConfig()));
        Replace<IModelManager>(services, new FakeModelManager(new FakeChatClient("raw transcribed text formatted.")));
        Replace<IInputInjectorService>(services, new CapturingInputInjectorService());
        Replace<IClipboardService>(services, new FakeClipboardService());
        Replace<IDialogService>(services, new FakeDialogService());
        Replace<IFileLogger>(services, new FakeFileLogger());
        Replace<ITranscriptionService>(services, new FakeTranscriptionService { FixedResult = "raw transcribed text" });

        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task FullPipeline_FormatsAndInjectsText()
    {
        var orchestrator = _provider.GetRequiredService<IPipelineOrchestrator>();
        var ui = _provider.GetRequiredService<IRecordingUIManager>() as FakeRecordingUIManager;
        var injector = _provider.GetRequiredService<IInputInjectorService>() as CapturingInputInjectorService;
        var clipboard = _provider.GetRequiredService<IClipboardService>() as FakeClipboardService;

        orchestrator.StartRecording();
        await Task.Delay(1100); // satisfy minimum 1-second recording duration
        orchestrator.StopRecordingAndProcess("standard");

        await ui!.CompletionTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Single(injector!.Calls);
        var call = injector.Calls[0];
        var actualText = call.Mode == InjectionMode.ClipboardPaste
            ? clipboard!.LastCopiedText
            : call.Text;
        Assert.Equal("raw transcribed text formatted.", actualText);
    }

    [Fact]
    public async Task RawMode_SkipsFormattingAndInjectsRawText()
    {
        var orchestrator = _provider.GetRequiredService<IPipelineOrchestrator>();
        var ui = _provider.GetRequiredService<IRecordingUIManager>() as FakeRecordingUIManager;
        var injector = _provider.GetRequiredService<IInputInjectorService>() as CapturingInputInjectorService;
        var clipboard = _provider.GetRequiredService<IClipboardService>() as FakeClipboardService;
        var config = _provider.GetRequiredService<IConfigService>() as FakeConfigService;

        config!.Load().Modes.First(m => m.Id == "raw");
        // raw mode has SkipFormatting = true, so chat model is bypassed

        orchestrator.StartRecording();
        await Task.Delay(1100);
        orchestrator.StopRecordingAndProcess("raw");

        await ui!.CompletionTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Single(injector!.Calls);
        var call = injector.Calls[0];
        var actualText = call.Mode == InjectionMode.ClipboardPaste
            ? clipboard!.LastCopiedText
            : call.Text;
        Assert.Equal("raw transcribed text", actualText);
    }

    [Fact]
    public async Task SnippetMatched_InjectsExpansionViaSendKeys()
    {
        var orchestrator = _provider.GetRequiredService<IPipelineOrchestrator>();
        var ui = _provider.GetRequiredService<IRecordingUIManager>() as FakeRecordingUIManager;
        var injector = _provider.GetRequiredService<IInputInjectorService>() as CapturingInputInjectorService;
        var config = _provider.GetRequiredService<IConfigService>() as FakeConfigService;
        var transcription = _provider.GetRequiredService<ITranscriptionService>() as FakeTranscriptionService;

        transcription!.FixedResult = "sig";
        config!.Load().Snippets.Add(new Snippet
        {
            Trigger = "sig",
            Expansion = "{ENTER}Best regards,{ENTER}John"
        });

        orchestrator.StartRecording();
        await Task.Delay(1100);
        orchestrator.StopRecordingAndProcess("standard");

        await ui!.CompletionTask.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Single(injector!.Calls);
        Assert.Equal(InjectionMode.SendKeys, injector.Calls[0].Mode);
        Assert.Equal("{ENTER}Best regards,{ENTER}John", injector.Calls[0].Text);
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
