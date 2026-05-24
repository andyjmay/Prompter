namespace Prompter.Services;

public class TranscriptionService : ITranscriptionService
{
    private readonly IModelManager _modelManager;
    private readonly IFileLogger _fileLogger;

    public TranscriptionService(IModelManager modelManager, IFileLogger fileLogger)
    {
        _modelManager = modelManager;
        _fileLogger = fileLogger;
    }

    public async Task<string> TranscribeAsync(string wavPath, string language, CancellationToken ct)
    {
        if (!_modelManager.WhisperReady)
            throw new InvalidOperationException("Whisper not loaded");

        var audioClient = await _modelManager.GetAudioClientAsync();
        audioClient.Settings.Language = language;

        var sb = new System.Text.StringBuilder();
        var stream = audioClient.TranscribeAudioStreamingAsync(wavPath, ct);
        await foreach (var chunk in stream)
        {
            ct.ThrowIfCancellationRequested();
            var text = chunk.Text.Replace("[BLANK_AUDIO]", string.Empty, StringComparison.OrdinalIgnoreCase);
            sb.Append(text);
        }

        _fileLogger.Log($"Transcription: {sb}");
        return sb.ToString();
    }
}
