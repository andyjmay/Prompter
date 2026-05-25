using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Prompter.Models;

namespace Prompter.Services;

public partial class FileLogger : IFileLogger
{
    private readonly string _logDir;
    private readonly object _lock = new();

    [GeneratedRegex(@"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}\.\d{3})\]\s(.*)$", RegexOptions.Compiled)]
    private static partial Regex LogLineRegex();

    public FileLogger()
    {
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prompter", "logs");
        Directory.CreateDirectory(_logDir);
        PurgeOldLogs();
    }

    internal FileLogger(string logDir)
    {
        _logDir = logDir;
        Directory.CreateDirectory(_logDir);
    }

    public void Log(string message)
    {
        try
        {
            var path = Path.Combine(_logDir, $"prompter-debug-{DateTime.Now:yyyyMMdd}.txt");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            lock (_lock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging is best-effort; do not crash the application.
        }
    }

    public void LogException(Exception ex, string context)
    {
        Log($"[ERROR] {context}: {ex.Message}\n{ex.StackTrace}");
    }

    public void ClearLogs()
    {
        try
        {
            lock (_lock)
            {
                foreach (var file in Directory.EnumerateFiles(_logDir, "prompter-debug-*.txt"))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    public IEnumerable<LogEntry> GetRecentLogs(int maxEntries = 5000)
    {
        var entries = new List<LogEntry>();
        var regex = LogLineRegex();

        try
        {
            var files = Directory.EnumerateFiles(_logDir, "prompter-debug-*.txt")
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var file in files)
            {
                if (entries.Count >= maxEntries)
                    break;

                string fileName = Path.GetFileName(file);
                string[] lines;
                lock (_lock)
                {
                    lines = File.ReadAllLines(file);
                }

                LogEntry? lastEntry = null;
                foreach (var line in lines)
                {
                    if (entries.Count >= maxEntries)
                        break;

                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        var timestamp = DateTime.ParseExact(match.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                        var message = match.Groups[2].Value;
                        lastEntry = new LogEntry
                        {
                            Timestamp = timestamp,
                            Message = message,
                            IsError = message.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase),
                            SourceFile = fileName
                        };
                        entries.Add(lastEntry);
                    }
                    else if (lastEntry != null)
                    {
                        var updated = lastEntry with { Message = lastEntry.Message + Environment.NewLine + line };
                        entries[entries.Count - 1] = updated;
                        lastEntry = updated;
                    }
                }
            }
        }
        catch
        {
            // Best-effort read
        }

        // Return most recent first
        entries.Reverse();
        return entries;
    }

    private void PurgeOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var file in Directory.EnumerateFiles(_logDir, "prompter-debug-*.txt"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
