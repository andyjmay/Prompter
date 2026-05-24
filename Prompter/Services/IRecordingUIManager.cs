namespace Prompter.Services;

public interface IRecordingUIManager : IDisposable
{
    void ShowRecordingOverlay();
    void HideRecordingOverlay();
    void UpdateAudioLevel(double normalizedLevel);
    void ShowPreviewToast(string text);
    void ShowBalloonIfEnabled(string title, string message);
    void TransitionOverlayToProcessing();
    void UpdateProcessingStage(string stageLabel);
}
