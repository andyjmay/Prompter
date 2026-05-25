using System.IO;
using NAudio.Wave;

namespace Prompter.Services;

public class AudioRecorderService : IAudioRecorderService
{
    private readonly IFileLogger _logger;
    private IRecordingSession? _activeSession;
    private readonly object _lock = new();
    private bool _disposed;

    public AudioRecorderService(IFileLogger logger)
    {
        _logger = logger;
    }

    public IRecordingSession StartRecording()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioRecorderService));
            if (_activeSession != null)
                throw new InvalidOperationException("A recording session is already active.");

            var session = new RecordingSession(_logger);
            _activeSession = session;
            session.RecordingError += _ => _activeSession = null;
            session.Disposed += () => _activeSession = null;
            return session;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _activeSession?.Dispose();
            _activeSession = null;
        }
    }
}

public class RecordingSession : IRecordingSession
{
    private readonly IFileLogger _logger;
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _tempPath;
    private bool _disposed;
    private readonly object _lock = new();

    public string? RecordedFilePath => _tempPath;

    public event Action<Exception>? RecordingError;
    public event Action<double>? AudioLevelAvailable;
    public event Action? Disposed;

    public RecordingSession(IFileLogger logger)
    {
        _logger = logger;
    }

    public void Begin()
    {
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
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "AudioRecorderService.StartRecording");
            RecordingError?.Invoke(ex);
            Cleanup();
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

            double level = ComputeAudioLevel(e.Buffer, e.BytesRecorded);
            AudioLevelAvailable?.Invoke(level);
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "AudioRecorderService.OnDataAvailable");
            RecordingError?.Invoke(ex);
        }
    }

    private static double ComputeAudioLevel(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded == 0) return 0;
        double sum = 0;
        int sampleCount = bytesRecorded / 2;
        for (int i = 0; i < bytesRecorded; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            double normalized = sample / 32768.0;
            sum += normalized * normalized;
        }
        double rms = Math.Sqrt(sum / sampleCount);
        // Scale up aggressively so normal speech fills most of the bar and quiet speech is still visible.
        return Math.Min(rms * 25.0 + 0.05, 1.0);
    }

    public void StopRecording()
    {
        lock (_lock)
        {
            StopRecordingCore();
        }
    }

    private void StopRecordingCore()
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

    private void Cleanup()
    {
        try
        {
            StopRecordingCore();
            if (_tempPath != null && File.Exists(_tempPath))
            {
                File.Delete(_tempPath);
                _tempPath = null;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            Cleanup();
        }
        Disposed?.Invoke();
    }
}
