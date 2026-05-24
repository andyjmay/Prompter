using Prompter.Models;

namespace Prompter.Services;

public interface IPipelineOrchestrator : IDisposable
{
    event Action<string>? OutputReady;
    event Action<string, string>? ShowBalloon;

    void StartRecording();
    void StopRecordingAndProcess(FormatMode mode);
}
