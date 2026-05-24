using System.Windows;

namespace Prompter.Services;

public class ClipboardService : IClipboardService
{
    private readonly IFileLogger _logger;

    public ClipboardService(IFileLogger logger)
    {
        _logger = logger;
    }

    public ClipboardSnapshot SaveClipboard()
    {
        try
        {
            var text = Clipboard.GetText();
            if (!string.IsNullOrEmpty(text))
            {
                _logger.Log("Clipboard snapshot saved (text-only).");
                return new ClipboardSnapshot { TextFallback = text };
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Text clipboard snapshot failed");
        }

        _logger.Log("Clipboard snapshot unavailable — will not restore.");
        return new ClipboardSnapshot();
    }

    public void RestoreClipboard(ClipboardSnapshot snapshot)
    {
        if (!snapshot.HasData)
        {
            _logger.Log("No clipboard snapshot to restore.");
            return;
        }

        try
        {
            if (snapshot.TextFallback != null)
            {
                Clipboard.SetText(snapshot.TextFallback);
                _logger.Log("Clipboard restored (text-only).");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Clipboard restore failed");
        }
    }

    public bool CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Clipboard.CopyText failed");
            return false;
        }
    }
}

public class ClipboardSnapshot
{
    public string? TextFallback { get; set; }
    public bool HasData => TextFallback != null;
}
