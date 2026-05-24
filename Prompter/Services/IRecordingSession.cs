using Prompter.Services;

namespace Prompter.Services;

public interface IRecordingSession : IDisposable
{
    string? RecordedFilePath { get; }
    event Action<Exception>? RecordingError;
    void Begin();
    void StopRecording();
}
