using System.Text;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeFileLogger : IFileLogger
{
    public StringBuilder LogBuilder { get; } = new();
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public void Log(string message)
    {
        lock (_lock)
        {
            LogBuilder.AppendLine(message);
            _entries.Add(new LogEntry { Timestamp = DateTime.Now, Message = message });
        }
    }

    public void LogException(Exception ex, string context)
    {
        var line = $"[{context}] {ex}";
        lock (_lock)
        {
            LogBuilder.AppendLine(line);
            _entries.Add(new LogEntry { Timestamp = DateTime.Now, Message = line });
        }
    }

    public IEnumerable<LogEntry> GetRecentLogs(int maxEntries = 5000)
    {
        lock (_lock)
        {
            return _entries.TakeLast(maxEntries).Reverse().ToList();
        }
    }

    public void ClearLogs()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
