namespace Prompter.Services;

public interface IRecordingUIManager : IDisposable
{
    void ShowRecordingOverlay();
    void HideRecordingOverlay();
    void ShowPreviewToast(string text);
    void ShowBalloonIfEnabled(string title, string message);
}
