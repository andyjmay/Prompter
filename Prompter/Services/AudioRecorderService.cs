using System.IO;
using NAudio.Wave;

namespace Prompter.Services;

public class AudioRecorderService : IDisposable
{
    private readonly FileLogger _logger;
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _tempPath;
    private readonly object _lock = new();
    private bool _disposed;

    public string? RecordedFilePath => _tempPath;

    public event Action<Exception>? RecordingError;

    public AudioRecorderService(FileLogger logger)
    {
        _logger = logger;
    }

    public bool StartRecording()
    {
        lock (_lock)
        {
            StopRecording();

            _tempPath = Path.Combine(Path.GetTempPath(), $"prompter-recording-{Guid.NewGuid()}.wav");
            _logger.Log($"Starting recording to {_tempPath}");

            try
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(rate: 16000, bits: 16, channels: 1),
                    BufferMilliseconds = 100
                };
                _writer = new WaveFileWriter(_tempPath, _waveIn.WaveFormat);

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.StartRecording();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "AudioRecorderService.StartRecording");
                RecordingError?.Invoke(ex);
                _tempPath = null;
                return false;
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            lock (_lock)
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "AudioRecorderService.OnDataAvailable");
            RecordingError?.Invoke(ex);
        }
    }

    public void StopRecording()
    {
        lock (_lock)
        {
            if (_waveIn != null)
            {
                _waveIn.StopRecording();
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.Dispose();
                _waveIn = null;
            }
            _writer?.Dispose();
            _writer = null;
            _logger.Log("Recording stopped.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopRecording();
        GC.SuppressFinalize(this);
    }
}
