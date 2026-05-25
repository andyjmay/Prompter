using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeRecordingUIManager : IRecordingUIManager
{
    private readonly TaskCompletionSource _tcs = new();

    public Task CompletionTask => _tcs.Task;

    public void ShowRecordingOverlay() { }

    public void HideRecordingOverlay() => _tcs.TrySetResult();

    public void UpdateAudioLevel(double normalizedLevel) { }

    public void ShowPreviewToast(string text) { }

    public void ShowBalloonIfEnabled(string title, string message) { }

    public void TransitionOverlayToProcessing() { }

    public void UpdateProcessingStage(string stageLabel) { }

    public void Dispose() => _tcs.TrySetResult();
}
