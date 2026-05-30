namespace Prompter.Services;

public interface IPipelineOrchestrator : IDisposable, IAsyncDisposable
{
    event Action<string>? OutputReady;
    event Action<string, string>? ShowBalloon;

    void StartRecording();
    void StopRecordingAndProcess(string modeId);
}
