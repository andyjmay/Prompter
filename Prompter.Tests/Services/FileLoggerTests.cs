using Prompter.Services;
using Xunit;

namespace Prompter.Tests;

public class FileLoggerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileLogger _logger;

    public FileLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logger = new FileLogger(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void Log_WritesTimestampedLine()
    {
        _logger.Log("Hello world");

        var files = Directory.GetFiles(_tempDir, "prompter-debug-*.txt");
        Assert.Single(files);
        var content = File.ReadAllText(files[0]);
        Assert.Contains("Hello world", content);
        Assert.Matches(@"\[\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}\.\d{3}\]", content);
    }

    [Fact]
    public void GetRecentLogs_ParsesSingleLine()
    {
        _logger.Log("Test message");

        var logs = _logger.GetRecentLogs().ToList();
        Assert.Single(logs);
        Assert.Equal("Test message", logs[0].Message);
        Assert.False(logs[0].IsError);
        Assert.Contains("prompter-debug-", logs[0].SourceFile);
    }

    [Fact]
    public void GetRecentLogs_SetsErrorFlag_ForErrorLines()
    {
        _logger.Log("[ERROR] Something broke");

        var logs = _logger.GetRecentLogs().ToList();
        Assert.Single(logs);
        Assert.True(logs[0].IsError);
    }

    [Fact]
    public void GetRecentLogs_MultiLineContinuation_CombinesIntoOneEntry()
    {
        // Single Log call with embedded newlines mimics LogException output
        _logger.Log("[ERROR] Exception: boom\n   at Foo.Bar() in C:\\src\\Foo.cs:line 10\n   at Baz.Qux() in C:\\src\\Baz.cs:line 20");

        var logs = _logger.GetRecentLogs().ToList();
        Assert.Single(logs);
        Assert.Contains("Exception: boom", logs[0].Message);
        Assert.Contains("at Foo.Bar()", logs[0].Message);
        Assert.Contains("at Baz.Qux()", logs[0].Message);
        Assert.True(logs[0].IsError);
    }

    [Fact]
    public void GetRecentLogs_RespectsMaxEntries()
    {
        for (int i = 0; i < 5; i++)
        {
            _logger.Log($"Message {i}");
        }

        var logs = _logger.GetRecentLogs(maxEntries: 2).ToList();
        Assert.Equal(2, logs.Count);
    }

    [Fact]
    public void LogException_CreatesErrorEntryWithStackTrace()
    {
        Exception ex;
        try
        {
            throw new InvalidOperationException("Bad state");
        }
        catch (Exception e)
        {
            ex = e;
        }
        _logger.LogException(ex, "Processing request");

        var logs = _logger.GetRecentLogs().ToList();
        Assert.Single(logs);
        Assert.True(logs[0].IsError);
        Assert.Contains("[ERROR] Processing request: Bad state", logs[0].Message);
        Assert.Contains("at Prompter.Tests.FileLoggerTests.LogException_CreatesErrorEntryWithStackTrace", logs[0].Message);
    }

    [Fact]
    public void ClearLogs_RemovesAllLogFiles()
    {
        _logger.Log("before clear");
        Assert.NotEmpty(Directory.GetFiles(_tempDir, "prompter-debug-*.txt"));

        _logger.ClearLogs();
        Assert.Empty(Directory.GetFiles(_tempDir, "prompter-debug-*.txt"));
    }

    [Fact]
    public void GetRecentLogs_ReturnsMostRecentFirst()
    {
        _logger.Log("First");
        _logger.Log("Second");
        _logger.Log("Third");

        var logs = _logger.GetRecentLogs().ToList();
        Assert.Equal(3, logs.Count);
        Assert.Equal("Third", logs[0].Message);
        Assert.Equal("Second", logs[1].Message);
        Assert.Equal("First", logs[2].Message);
    }

    [Fact]
    public void GetRecentLogs_HandlesPlainLinesWithoutTimestamp()
    {
        var file = Path.Combine(_tempDir, $"prompter-debug-{DateTime.Now:yyyyMMdd}.txt");
        File.WriteAllText(file, "some plain text without timestamp\n");

        var logs = _logger.GetRecentLogs().ToList();
        Assert.Empty(logs);
    }

    [Fact]
    public void Log_IsBestEffort_DoesNotThrow()
    {
        // Even if we somehow pass an invalid path, Log swallows exceptions.
        // We already test happy path; this documents the no-throw contract.
        var ex = Record.Exception(() => _logger.Log("safe"));
        Assert.Null(ex);
    }

    [Fact]
    public void PurgeOldLogs_DeletesFilesOlderThanSevenDays()
    {
        var oldDate = DateTime.Now.AddDays(-8);
        var oldFile = Path.Combine(_tempDir, $"prompter-debug-{oldDate:yyyyMMdd}.txt");
        File.WriteAllText(oldFile, "old log content");
        File.SetLastWriteTime(oldFile, oldDate);

        _logger.PurgeOldLogs();

        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void PurgeOldLogs_PreservesRecentFiles()
    {
        var todayFile = Path.Combine(_tempDir, $"prompter-debug-{DateTime.Now:yyyyMMdd}.txt");
        File.WriteAllText(todayFile, "recent log content");

        _logger.PurgeOldLogs();

        Assert.True(File.Exists(todayFile));
    }
}
