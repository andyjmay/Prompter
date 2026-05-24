namespace Prompter.Services;

public interface IAudioRecorderService : IDisposable
{
    IRecordingSession StartRecording();
}
