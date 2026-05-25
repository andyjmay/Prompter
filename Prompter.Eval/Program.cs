using Microsoft.Extensions.DependencyInjection;
using Prompter.Eval.Dataset;
using Prompter.Eval.Fakes;
using Prompter.Eval.Processing;
using Prompter.Eval.Reporting;
using Prompter.Eval.Scoring;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Eval;

public class Program
{
    public static async Task Main(string[] args)
    {
        bool smoke = args.Length == 0 || args.Contains("--smoke");
        bool full = args.Contains("--full");

        Console.WriteLine("Prompter Eval — Model Quality Benchmark");
        if (smoke && !full)
        {
            Console.WriteLine("Mode: smoke (quick sanity check). Use --full for exhaustive grid.");
        }
        else
        {
            Console.WriteLine("Mode: full (exhaustive grid — this will take a while).");
        }
        Console.WriteLine();

        var dataset = DatasetLoader.LoadDefault();
        var scorer = new CompositeScorer();

        var services = BuildServices();
        var provider = services.BuildServiceProvider();

        // Initialize Foundry Local before any model access
        Console.WriteLine("Initializing Foundry Local...");
        var modelManager = provider.GetRequiredService<IModelManager>();
        try
        {
            await modelManager.InitializeAsync(5);
            Console.WriteLine("Foundry Local initialized.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Foundry Local initialization failed: {ex.Message}");
        }

        var configs = await BuildConfigsAsync(provider, smoke);
        var runner = provider.GetRequiredService<EvalRunner>();
        var results = new List<EvalResult>();

        // Group by chat model so each model loads once and stays resident
        var groupedByChat = configs
            .GroupBy(c => c.UseCustomChat
                ? c.CustomChatModelPath ?? "custom"
                : c.ChatModelAlias ?? "none")
            .ToList();

        foreach (var chatGroup in groupedByChat)
        {
            var chatKey = chatGroup.Key;
            var chatConfigs = chatGroup.ToList();

            var first = chatConfigs.First();
            var chatLabel = first.UseCustomChat
                ? Path.GetFileName(first.CustomChatModelPath ?? "custom")
                : first.ChatModelAlias ?? "none";

            Console.WriteLine($"Chat model: {chatLabel}");
            Console.WriteLine(new string('-', 40));

            foreach (var config in chatConfigs)
            {
                var configLabel = $"  {config.WhisperModelAlias} / {chatLabel} / {config.ModeId}";
                Console.WriteLine(configLabel);

                foreach (var testCase in dataset.Cases)
                {
                    Console.WriteLine($"    Case: {testCase.Id} ... ");
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var pipelineResult = await runner.RunAsync(config, testCase);
                        sw.Stop();

                        var scores = scorer.Score(
                            pipelineResult.RawText,
                            pipelineResult.FormattedText,
                            pipelineResult.FinalText,
                            testCase,
                            config.ModeId);

                        results.Add(new EvalResult(
                            configLabel.Trim(),
                            pipelineResult.LoadedWhisperAlias,
                            pipelineResult.LoadedChatAlias,
                            config.ModeId,
                            testCase.Id,
                            testCase.Phrase,
                            pipelineResult,
                            scores,
                            sw.Elapsed));

                        Console.WriteLine($"      Transcription={scores.TranscriptionAccuracy:F3} Formatting={scores.FormattingFidelity:F3} Overall={scores.Overall:F3} ({sw.Elapsed:ss\\.fff}s)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      FAILED: {ex.Message}");
                    }
                }
            }

            Console.WriteLine();
        }

        var reporter = new ConsoleReporter();
        reporter.Report(results);

        await provider.DisposeAsync();
    }

    private static async Task<List<EvalConfig>> BuildConfigsAsync(IServiceProvider provider, bool smoke)
    {
        if (smoke)
        {
            return new List<EvalConfig>
            {
                new EvalConfig("whisper-tiny", "phi-3.5-mini", "standard"),
                new EvalConfig("whisper-tiny", "phi-3.5-mini", "code")
            };
        }

        var whisperModels = new[] { "whisper-tiny", "whisper-tiny-en", "whisper-base" };
        var chatModels = new[] { "phi-3.5-mini", "gemma-3-270m-it" };
        var modes = new[] { "standard", "formal", "raw", "code" };

        var configs = new List<EvalConfig>();

        // Build Foundry Local configs: outer loop is chat model so it stays loaded
        foreach (var chat in chatModels)
        {
            foreach (var whisper in whisperModels)
            {
                foreach (var mode in modes)
                {
                    if (mode == "raw")
                    {
                        configs.Add(new EvalConfig(whisper, null, mode));
                    }
                    else
                    {
                        configs.Add(new EvalConfig(whisper, chat, mode));
                    }
                }
            }
        }

        // Custom GGUF configs (if any are installed)
        var ggufStore = provider.GetService<IGgufModelStore>();
        if (ggufStore != null)
        {
            try
            {
                var installed = await ggufStore.GetInstalledModelsAsync(CancellationToken.None);
                foreach (var gguf in installed)
                {
                    foreach (var whisper in whisperModels)
                    {
                        foreach (var mode in modes)
                        {
                            if (mode == "raw")
                            {
                                continue;
                            }
                            configs.Add(new EvalConfig(whisper, null, mode, UseCustomChat: true, CustomChatModelPath: gguf.FullPath));
                        }
                    }
                }

                if (installed.Count > 0)
                {
                    Console.WriteLine($"Discovered {installed.Count} custom GGUF model(s):");
                    foreach (var gguf in installed)
                    {
                        Console.WriteLine($"  - {gguf.FileName} ({gguf.FullPath})");
                    }
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not discover custom GGUF models: {ex.Message}");
            }
        }

        return configs;
    }

    private static ServiceCollection BuildServices()
    {
        var services = new ServiceCollection();

        var config = new AppConfig
        {
            WhisperModelId = "whisper-tiny",
            ChatModelId = "phi-3.5-mini",
            UseClipboardPaste = false,
            PasteThresholdCharacters = 9999,
            ProcessingTimeoutSeconds = 300
        };
        services.AddSingleton<IConfigService>(new FakeConfigService(config));

        services.AddSingleton<IFileLogger, FileLogger>();
        services.AddSingleton<IFoundryLocalManagerAccessor, FoundryLocalManagerAccessor>();
        services.AddSingleton<FoundryTranscriptionProvider>();
        services.AddSingleton<WhisperNetTranscriptionProvider>();
        services.AddSingleton<IModelManager, ModelManager>();
        services.AddSingleton<ITranscriptionService, TranscriptionService>();
        services.AddSingleton<ITextFormatter, TextFormatter>();
        services.AddSingleton<ISnippetMatcher, SnippetMatcher>();
        services.AddSingleton<IModelCatalogService, ModelCatalogService>();
        services.AddSingleton<IHuggingFaceService, HuggingFaceService>();
        services.AddSingleton<IGgufModelStore, GgufModelStore>();
        services.AddSingleton<IPipelineProcessor, PipelineProcessor>();

        services.AddSingleton<EvalRunner>();

        return services;
    }
}
