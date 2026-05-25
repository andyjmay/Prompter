using Prompter.Models;
using Prompter.Services;
using Xunit;

namespace Prompter.Tests;

public class SpokenPunctuationProcessorTests
{
    private static IFileLogger NoOpLogger => new FakeFileLogger();

    [Theory]
    [InlineData("exclamation point", "!", "TrailingSpace")]
    [InlineData("exclamation mark", "!", "TrailingSpace")]
    [InlineData("question mark", "?", "TrailingSpace")]
    [InlineData("open quote", "\"", "OpenQuote")]
    [InlineData("close quote", "\"", "CloseQuote")]
    [InlineData("new paragraph", "\n\n", "Structural")]
    [InlineData("new line", "\n", "Structural")]
    [InlineData("ellipsis", "...", "TrailingSpace")]
    [InlineData("semicolon", ";", "TrailingSpace")]
    [InlineData("period", ".", "TrailingSpace")]
    [InlineData("comma", ",", "TrailingSpace")]
    [InlineData("colon", ":", "TrailingSpace")]
    [InlineData("dash", "-", "TrailingSpace")]
    [InlineData("tab", "\t", "Structural")]
    [InlineData("at sign", "@", "TrailingSpace")]
    [InlineData("hashtag", "#", "TrailingSpace")]
    public void Process_ReplacesToken(string token, string replacement, string spacing)
    {
        var input = $"Hello {token} world";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);

        // Structural tokens consume surrounding whitespace; non-structural retains trailing space.
        // OpenQuote also retains leading space because the regex consumes it and the replacement prepends it.
        var expected = spacing == "Structural"
            ? $"Hello{replacement.Replace("\n", Environment.NewLine)}world"
            : spacing == "OpenQuote"
                ? $"Hello {replacement} world"
                : $"Hello{replacement} world";

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Process_TrailingSpace_RetainsFollowingSpace()
    {
        var input = "Hello comma world";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal("Hello, world", result);
    }

    [Fact]
    public void Process_OpenQuote_PreservesLeadingSpace()
    {
        var input = "Hello open quote world";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal("Hello \" world", result);
    }

    [Fact]
    public void Process_CloseQuote_PreservesTrailingSpace()
    {
        var input = "Hello close quote world";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal("Hello\" world", result);
    }

    [Fact]
    public void Process_Structural_ConsumesSurroundingWhitespace()
    {
        var input = "Hello new paragraph world";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal($"Hello{Environment.NewLine}{Environment.NewLine}world", result);
    }

    [Fact]
    public void Process_CaseInsensitive()
    {
        var input = "Hello PERIOD world";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal("Hello. world", result);
    }

    [Fact]
    public void Process_DoesNotMatchInsideWords()
    {
        var input = "The periodic table is fantastic";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal("The periodic table is fantastic", result);
    }

    [Fact]
    public void Process_NonEnglish_ReturnsInputUnchanged()
    {
        var input = "Hello period world";
        var result = SpokenPunctuationProcessor.Process(input, "fr-FR", NoOpLogger);
        Assert.Equal("Hello period world", result);
    }

    [Fact]
    public void Process_EmptyInput_ReturnsEmpty()
    {
        var result = SpokenPunctuationProcessor.Process("", "en-US", NoOpLogger);
        Assert.Equal("", result);
    }

    [Fact]
    public void Process_NullOrWhitespace_ReturnsUnchanged()
    {
        var result = SpokenPunctuationProcessor.Process("   ", "en-US", NoOpLogger);
        Assert.Equal("   ", result);
    }

    [Fact]
    public void Process_MultipleTokens_InOneString()
    {
        var input = "Hello comma world period new paragraph goodbye";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal($"Hello, world.{Environment.NewLine}{Environment.NewLine}goodbye", result);
    }

    [Fact]
    public void Process_ExclamationPoint_OverExclamationMark()
    {
        // "exclamation point" is longer (17) than "exclamation mark" (16)
        // Both should work; this test just verifies no collision
        var input = "exclamation point";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal("!", result);
    }

    [Fact]
    public void Process_LogsAppliedTokens()
    {
        var logger = new FakeFileLogger();
        var input = "Hello comma world period";
        _ = SpokenPunctuationProcessor.Process(input, "en-US", logger);

        Assert.True(logger.Messages.Count >= 1);
        var log = logger.Messages.First();
        Assert.Contains("comma", log);
        Assert.Contains("period", log);
    }

    [Fact]
    public void Process_DoesNotMatchWithArticlePrefix()
    {
        var input = "a comma is nice";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal("a comma is nice", result);
    }

    [Fact]
    public void Process_MatchesAtStartOfString()
    {
        var input = "period hello";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal(". hello", result);
    }

    [Fact]
    public void Process_MatchesAtEndOfString()
    {
        var input = "hello period";
        var result = SpokenPunctuationProcessor.Process(input, "en-US", NoOpLogger);
        Assert.Equal("hello.", result);
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
