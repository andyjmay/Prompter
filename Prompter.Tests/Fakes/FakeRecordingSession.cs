using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeRecordingSession : IRecordingSession
{
    private readonly string _wavPath;

    public string? RecordedFilePath => _wavPath;

#pragma warning disable CS0067
    public event Action<Exception>? RecordingError;
#pragma warning restore CS0067
    public event Action<double>? AudioLevelAvailable;
    public event Action? Disposed;

    public FakeRecordingSession(string wavPath)
    {
        _wavPath = wavPath;
    }

    public void Begin()
    {
        // Simulate a brief audio level pulse so the overlay has something to show.
        AudioLevelAvailable?.Invoke(0.5);
    }

    public void StopRecording() { }

    public void Dispose() => Disposed?.Invoke();
}
