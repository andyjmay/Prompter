using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeAudioRecorderService : IAudioRecorderService
{
    private readonly string _sourceWavPath;

    public FakeAudioRecorderService(string sourceWavPath)
    {
        _sourceWavPath = sourceWavPath;
    }

    public IRecordingSession StartRecording()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"prompter-test-{Guid.NewGuid()}.wav");
        File.Copy(_sourceWavPath, tempPath, overwrite: true);
        return new FakeRecordingSession(tempPath);
    }

    public void Dispose() { }
}
