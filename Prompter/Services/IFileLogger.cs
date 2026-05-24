using System.IO;

namespace Prompter.Services;

public interface IFileLogger
{
    void Log(string message);
    void LogException(Exception ex, string context);
}
