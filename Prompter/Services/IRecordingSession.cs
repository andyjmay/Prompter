using Prompter.Services;

namespace Prompter.Services;

public interface IRecordingSession : IDisposable
{
    string? RecordedFilePath { get; }
    event Action<Exception>? RecordingError;
    event Action<double>? AudioLevelAvailable;
    void Begin();
    void StopRecording();
}
