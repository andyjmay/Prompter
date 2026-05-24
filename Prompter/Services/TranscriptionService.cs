namespace Prompter.Services;

public class TranscriptionService : ITranscriptionService
{
    private readonly IFoundryLocalManagerAccessor _accessor;
    private readonly IModelManager _modelManager;
    private readonly IFileLogger _fileLogger;

    public TranscriptionService(IFoundryLocalManagerAccessor accessor, IModelManager modelManager, IFileLogger fileLogger)
    {
        _accessor = accessor;
        _modelManager = modelManager;
        _fileLogger = fileLogger;
    }

    public async Task<string> TranscribeAsync(string wavPath, string language, CancellationToken ct)
    {
        if (!_modelManager.WhisperReady || _modelManager.LoadedWhisperModelAlias == null)
            throw new InvalidOperationException("Whisper not loaded");

        var catalog = await _accessor.Manager.GetCatalogAsync();
        var whisperModel = await catalog.GetModelAsync(_modelManager.LoadedWhisperModelAlias);
        if (whisperModel == null)
            throw new InvalidOperationException("Whisper model not available in catalog");

        var audioClient = await whisperModel.GetAudioClientAsync();
        audioClient.Settings.Language = language;

        var sb = new System.Text.StringBuilder();
        var stream = audioClient.TranscribeAudioStreamingAsync(wavPath, ct);
        await foreach (var chunk in stream)
        {
            ct.ThrowIfCancellationRequested();
            sb.Append(chunk.Text);
        }

        _fileLogger.Log($"Transcription: {sb}");
        return sb.ToString();
    }
}
