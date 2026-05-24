using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Prompter.Services;

public class FoundryTranscriptionProvider : ITranscriptionProvider
{
    private readonly IModelManager _modelManager;
    private readonly IFileLogger _fileLogger;

    public bool IsLoaded => _modelManager.WhisperReady;

    public FoundryTranscriptionProvider(IModelManager modelManager, IFileLogger fileLogger)
    {
        _modelManager = modelManager;
        _fileLogger = fileLogger;
    }

    public Task LoadAsync(CancellationToken ct = default)
    {
        // ModelManager handles loading of standard models via EnsureModelsLoadedAsync.
        return Task.CompletedTask;
    }

    public Task UnloadAsync()
    {
        // Unloading is handled by ModelManager calling UnloadWhisperModelAsync.
        return Task.CompletedTask;
    }

    public async Task<string> TranscribeAsync(string wavPath, string language, CancellationToken ct)
    {
        if (!_modelManager.WhisperReady)
            throw new InvalidOperationException("Whisper not loaded");

        var audioClient = await _modelManager.GetAudioClientAsync();
        audioClient.Settings.Language = language;

        var sb = new StringBuilder();
        var stream = audioClient.TranscribeAudioStreamingAsync(wavPath, ct);
        await foreach (var chunk in stream)
        {
            ct.ThrowIfCancellationRequested();
            var text = chunk.Text.Replace("[BLANK_AUDIO]", string.Empty, StringComparison.OrdinalIgnoreCase);
            sb.Append(text);
        }

        _fileLogger.Log($"Foundry transcription: {sb}");
        return sb.ToString();
    }
}
