using System.Runtime.InteropServices;
using System.Windows;

namespace Prompter.Services;

public class ClipboardService
{
    private readonly FileLogger _logger;

    public ClipboardService(FileLogger logger)
    {
        _logger = logger;
    }

    public ClipboardSnapshot SaveClipboard()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null) return new ClipboardSnapshot();

            // Try full snapshot
            var formats = data.GetFormats();
            var entries = new Dictionary<string, object?>();
            foreach (var fmt in formats)
            {
                try
                {
                    var val = data.GetData(fmt);
                    if (val != null) entries[fmt] = val;
                }
                catch { /* some formats are non-serializable */ }
            }

            if (entries.Count > 0)
            {
                _logger.Log("Clipboard snapshot saved (full).");
                return new ClipboardSnapshot { FullSnapshot = entries };
            }
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "Full clipboard snapshot failed");
        }

        // Fallback: text only
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
            if (snapshot.FullSnapshot != null)
            {
                var data = new DataObject();
                foreach (var kv in snapshot.FullSnapshot)
                {
                    if (kv.Value != null)
                        data.SetData(kv.Key, kv.Value);
                }
                Clipboard.SetDataObject(data, true);
                _logger.Log("Clipboard restored (full).");
                return;
            }

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
    public Dictionary<string, object?>? FullSnapshot { get; set; }
    public string? TextFallback { get; set; }
    public bool HasData => FullSnapshot != null || TextFallback != null;
}
