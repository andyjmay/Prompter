using System.Diagnostics;
using Prompter.Eval.Dataset;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Eval.Processing;

public class EvalRunner
{
    private readonly IPipelineProcessor _processor;
    private readonly IConfigService _configService;
    private readonly IModelManager _modelManager;
    private readonly IFileLogger _logger;

    public EvalRunner(IPipelineProcessor processor, IConfigService configService, IModelManager modelManager, IFileLogger logger)
    {
        _processor = processor;
        _configService = configService;
        _modelManager = modelManager;
        _logger = logger;
    }

    public async Task<PipelineResult> RunAsync(EvalConfig config, EvalCase testCase)
    {
        var cfg = _configService.Load();

        var mutable = cfg with
        {
            WhisperModelId = config.WhisperModelAlias,
            ChatModelId = config.ChatModelAlias ?? cfg.ChatModelId,
            UseCustomWhisper = false,
            UseCustomChat = config.UseCustomChat,
            CustomChatModelPath = config.CustomChatModelPath ?? ""
        };

        if (_configService is Prompter.Eval.Fakes.FakeConfigService fakeConfig)
        {
            await fakeConfig.SaveAsync(mutable);
        }
        else
        {
            throw new InvalidOperationException("Eval requires a mutable FakeConfigService to swap model configs.");
        }

        var wavPath = AudioGenerator.EnsureAudioFile(testCase.Phrase);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cfg.ProcessingTimeoutSeconds));
        var result = await _processor.ProcessAsync(wavPath, config.ModeId, cts.Token);

        return result;
    }
}
