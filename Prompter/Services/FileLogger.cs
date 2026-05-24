using System.IO;

namespace Prompter.Services;

public class FileLogger : IFileLogger
{
    private readonly string _logDir;
    private readonly object _lock = new();

    public FileLogger()
    {
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prompter", "logs");
        Directory.CreateDirectory(_logDir);
        PurgeOldLogs();
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
