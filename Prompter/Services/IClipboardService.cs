namespace Prompter.Services;

public interface IClipboardService
{
    ClipboardSnapshot SaveClipboard();
    void RestoreClipboard(ClipboardSnapshot snapshot);
    bool CopyText(string text);
}
