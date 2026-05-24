using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace Prompter.Services;

public class WhisperNetTranscriptionProvider : ITranscriptionProvider, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IFileLogger _fileLogger;
    private WhisperFactory? _factory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public bool IsLoaded => _factory != null;

    public WhisperNetTranscriptionProvider(IConfigService configService, IFileLogger fileLogger)
    {
        _configService = configService;
        _fileLogger = fileLogger;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WhisperNetTranscriptionProvider));
            if (_factory != null) return;

            var cfg = _configService.Load();
            var modelPath = cfg.CustomWhisperModelPath;

            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Custom Whisper model file not found at: {modelPath}");
            }

            _fileLogger.Log($"Loading Whisper.net model from: {modelPath}");
            _factory = WhisperFactory.FromPath(modelPath);
            _fileLogger.Log("Whisper.net model loaded successfully.");
        }
        catch (Exception ex)
        {
            _fileLogger.LogException(ex, "Failed to load Whisper.net model");
            _factory = null;
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UnloadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_factory != null)
            {
                _factory.Dispose();
                _factory = null;
                _fileLogger.Log("Whisper.net model unloaded.");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> TranscribeAsync(string wavPath, string language, CancellationToken ct)
    {
        if (_factory == null)
        {
            await LoadAsync(ct);
        }

        if (_factory == null)
        {
            throw new InvalidOperationException("Whisper.net model is not loaded.");
        }

        try
        {
            _fileLogger.Log($"Starting Whisper.net transcription for: {wavPath} (Language: {language})");

            using var processor = _factory.CreateBuilder()
                .WithLanguage(language)
                .Build();

            using var fileStream = File.OpenRead(wavPath);
            var sb = new StringBuilder();

            await foreach (var result in processor.ProcessAsync(fileStream, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(result.Text))
                {
                    sb.Append(result.Text);
                }
            }

            _fileLogger.Log($"Whisper.net transcription completed. Length: {sb.Length}");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _fileLogger.LogException(ex, "Whisper.net transcription failed");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _factory?.Dispose();
        _factory = null;
        _lock.Dispose();
    }
}
