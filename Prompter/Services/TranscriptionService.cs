using System;
using System.Threading;
using System.Threading.Tasks;

namespace Prompter.Services;

public class TranscriptionService : ITranscriptionService
{
    private readonly IConfigService _configService;
    private readonly FoundryTranscriptionProvider _foundryProvider;
    private readonly WhisperNetTranscriptionProvider _whisperNetProvider;
    private readonly IFileLogger _fileLogger;

    public TranscriptionService(
        IConfigService configService,
        FoundryTranscriptionProvider foundryProvider,
        WhisperNetTranscriptionProvider whisperNetProvider,
        IFileLogger fileLogger)
    {
        _configService = configService;
        _foundryProvider = foundryProvider;
        _whisperNetProvider = whisperNetProvider;
        _fileLogger = fileLogger;
    }

    public async Task<string> TranscribeAsync(string wavPath, string language, CancellationToken ct)
    {
        var cfg = _configService.Load();
        ITranscriptionProvider provider = cfg.UseCustomWhisper
            ? _whisperNetProvider
            : _foundryProvider;

        _fileLogger.Log($"Routing transcription to {(cfg.UseCustomWhisper ? "Whisper.net" : "Foundry.Local")}");
        return await provider.TranscribeAsync(wavPath, language, ct);
    }
}
