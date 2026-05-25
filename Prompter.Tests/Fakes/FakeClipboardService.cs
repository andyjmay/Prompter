using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeClipboardService : IClipboardService
{
    public string? LastCopiedText { get; private set; }

    public ClipboardSnapshot SaveClipboard() => new();

    public void RestoreClipboard(ClipboardSnapshot snapshot) { }

    public bool CopyText(string text)
    {
        LastCopiedText = text;
        return true;
    }
}
