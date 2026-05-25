using Prompter.Models;
using Prompter.Services;
using Xunit;

namespace Prompter.Tests;

public class PersonalDictionaryProcessorTests
{
    private static IFileLogger NoOpLogger => new FakeFileLogger();

    [Fact]
    public void Process_EmptyInput_ReturnsEmpty()
    {
        var entries = new List<DictionaryEntry> { new() { Word = "foo", Aliases = new() { "bar" } } };
        var result = PersonalDictionaryProcessor.Process("", entries, NoOpLogger);
        Assert.Equal("", result);
    }

    [Fact]
    public void Process_EmptyEntries_ReturnsInputUnchanged()
    {
        var result = PersonalDictionaryProcessor.Process("hello world", new List<DictionaryEntry>(), NoOpLogger);
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Process_SingleAlias_ReplacesCorrectly()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "OpenAI", Aliases = new() { "open ai" } },
        };
        var result = PersonalDictionaryProcessor.Process("I use open ai daily", entries, NoOpLogger);
        Assert.Equal("I use OpenAI daily", result);
    }

    [Fact]
    public void Process_MultipleAliases_ReplacesAll()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "OpenAI", Aliases = new() { "open ai" } },
            new() { Word = "GitHub", Aliases = new() { "github", "git hub" } },
        };
        var result = PersonalDictionaryProcessor.Process("open ai and git hub are great", entries, NoOpLogger);
        Assert.Equal("OpenAI and GitHub are great", result);
    }

    [Fact]
    public void Process_CaseInsensitive()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "NASA", Aliases = new() { "nasa" } },
        };
        var result = PersonalDictionaryProcessor.Process("I love NASA and nasa", entries, NoOpLogger);
        Assert.Equal("I love NASA and NASA", result);
    }

    [Fact]
    public void Process_LongestAliasFirst_PrefersLongerMatch()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "CAT", Aliases = new() { "cat" } },
            new() { Word = "Caterpillar", Aliases = new() { "caterpillar" } },
        };
        var result = PersonalDictionaryProcessor.Process("I saw a caterpillar", entries, NoOpLogger);
        Assert.Equal("I saw a Caterpillar", result);
    }

    [Fact]
    public void Process_DuplicateAlias_FirstWinsAndLogsWarning()
    {
        var logger = new FakeFileLogger();
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "Alpha", Aliases = new() { "a" } },
            new() { Word = "Beta", Aliases = new() { "a" } },
        };
        var result = PersonalDictionaryProcessor.Process("a is the first letter", entries, logger);

        Assert.Equal("Alpha is the first letter", result);
        Assert.Contains(logger.Messages, m => m.Contains("duplicate alias 'a'"));
    }

    [Fact]
    public void Process_DoesNotReplaceInsideWords()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "cat", Aliases = new() { "cat" } },
        };
        var result = PersonalDictionaryProcessor.Process("The category is cathartic", entries, NoOpLogger);
        Assert.Equal("The category is cathartic", result);
    }

    [Fact]
    public void Process_EmptyWord_SkipsEntry()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "", Aliases = new() { "foo" } },
            new() { Word = "Bar", Aliases = new() { "foo" } },
        };
        var result = PersonalDictionaryProcessor.Process("foo is good", entries, NoOpLogger);
        Assert.Equal("Bar is good", result);
    }

    [Fact]
    public void Process_EmptyAlias_SkipsAlias()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "Bar", Aliases = new() { "", "foo" } },
        };
        var result = PersonalDictionaryProcessor.Process("foo is good", entries, NoOpLogger);
        Assert.Equal("Bar is good", result);
    }

    [Fact]
    public void Process_WhitespaceAlias_SkipsAlias()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "Bar", Aliases = new() { "   ", "foo" } },
        };
        var result = PersonalDictionaryProcessor.Process("foo is good", entries, NoOpLogger);
        Assert.Equal("Bar is good", result);
    }

    [Fact]
    public void Process_LogsAppliedReplacements()
    {
        var logger = new FakeFileLogger();
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "Rust", Aliases = new() { "rust" } },
            new() { Word = "Go", Aliases = new() { "go" } },
        };
        _ = PersonalDictionaryProcessor.Process("rust and go are languages", entries, logger);

        Assert.True(logger.Messages.Count >= 1);
        var log = logger.Messages.First();
        Assert.Contains("Rust", log);
        Assert.Contains("Go", log);
    }

    [Fact]
    public void Process_NoAliases_ReturnsInputUnchanged()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "Foo", Aliases = new() },
        };
        var result = PersonalDictionaryProcessor.Process("foo bar", entries, NoOpLogger);
        Assert.Equal("foo bar", result);
    }

    [Fact]
    public void Process_AliasAtStartOfString()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "Hello", Aliases = new() { "hi" } },
        };
        var result = PersonalDictionaryProcessor.Process("hi world", entries, NoOpLogger);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void Process_AliasAtEndOfString()
    {
        var entries = new List<DictionaryEntry>
        {
            new() { Word = "World", Aliases = new() { "world" } },
        };
        var result = PersonalDictionaryProcessor.Process("hello world", entries, NoOpLogger);
        Assert.Equal("hello World", result);
    }

    private sealed class FakeFileLogger : IFileLogger
    {
        public List<string> Messages { get; } = new();

        public void Log(string message) => Messages.Add(message);

        public void LogException(Exception ex, string context)
        {
        }

        public IEnumerable<LogEntry> GetRecentLogs(int maxEntries = 5000) => Array.Empty<LogEntry>();

        public void ClearLogs()
        {
        }
    }
}
