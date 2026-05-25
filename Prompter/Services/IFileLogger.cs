using System.Collections.Generic;
using Prompter.Models;

namespace Prompter.Services;

public interface IFileLogger
{
    void Log(string message);
    void LogException(Exception ex, string context);
    IEnumerable<LogEntry> GetRecentLogs(int maxEntries = 5000);
    void ClearLogs();
}
