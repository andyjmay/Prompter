using System.Text;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeFileLogger : IFileLogger
{
    public StringBuilder LogBuilder { get; } = new();
    private readonly List<LogEntry> _entries = new();

    public void Log(string message)
    {
        LogBuilder.AppendLine(message);
        _entries.Add(new LogEntry { Timestamp = DateTime.Now, Message = message });
    }

    public void LogException(Exception ex, string context)
    {
        var line = $"[{context}] {ex}";
        LogBuilder.AppendLine(line);
        _entries.Add(new LogEntry { Timestamp = DateTime.Now, Message = line });
    }

    public IEnumerable<LogEntry> GetRecentLogs(int maxEntries = 5000)
        => _entries.TakeLast(maxEntries);

    public void ClearLogs() => _entries.Clear();
}
