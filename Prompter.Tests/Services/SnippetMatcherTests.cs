using Prompter.Models;
using Prompter.Services;
using Xunit;

namespace Prompter.Tests;

public class SnippetMatcherTests
{
    private readonly SnippetMatcher _matcher = new();

    [Theory]
    [InlineData("email sig", "email sig", true)]
    [InlineData("emil sig", "email sig", true)]
    [InlineData("EMAIL SIG", "email sig", true)]
    [InlineData("email sig!", "email sig", true)]
    [InlineData("my email sig", "email sig", false)]
    [InlineData("email", "email sig", false)]
    [InlineData("sign off", "email sig", false)]
    public void Match_ExactAndNearMatches(string input, string trigger, bool shouldMatch)
    {
        var snippets = new List<Snippet> { new() { Trigger = trigger, Expansion = "expanded" } };

        var result = _matcher.Match(input, snippets);

        if (shouldMatch)
        {
            Assert.NotNull(result);
            Assert.Equal("expanded", result.Expansion);
        }
        else
        {
            Assert.Null(result);
        }
    }

    [Fact]
    public void Match_TieBreak_ByLongerTrigger()
    {
        var snippets = new List<Snippet>
        {
            new() { Trigger = "sig", Expansion = "short" },
            new() { Trigger = "email sig", Expansion = "long" },
        };

        var result = _matcher.Match("email sig", snippets);

        Assert.NotNull(result);
        Assert.Equal("long", result.Expansion);
    }

    [Fact]
    public void Match_TieBreak_ByEarlierIndex()
    {
        var snippets = new List<Snippet>
        {
            new() { Trigger = "foo", Expansion = "first" },
            new() { Trigger = "foo", Expansion = "second" },
        };

        var result = _matcher.Match("foo", snippets);

        Assert.NotNull(result);
        Assert.Equal("first", result.Expansion);
    }

    [Fact]
    public void Match_LevenshteinDistanceWithinTwo()
    {
        var snippets = new List<Snippet>
        {
            new() { Trigger = "meeting notes", Expansion = "notes" },
        };

        // Two typos: "meating" instead of "meeting", missing space
        var result = _matcher.Match("meatingnotes", snippets);

        // Normalization collapses whitespace; "meatingnotes" vs "meetingnotes" distance is 1
        Assert.NotNull(result);
        Assert.Equal("notes", result.Expansion);
    }

    [Fact]
    public void Match_DistanceExceedsTwo_ReturnsNull()
    {
        var snippets = new List<Snippet>
        {
            new() { Trigger = "meeting notes", Expansion = "notes" },
        };

        var result = _matcher.Match("something completely different", snippets);

        Assert.Null(result);
    }

    [Fact]
    public void Match_EmptyInput_ReturnsNull()
    {
        var result = _matcher.Match("", new List<Snippet> { new() { Trigger = "foo", Expansion = "bar" } });
        Assert.Null(result);
    }

    [Fact]
    public void Match_EmptySnippetList_ReturnsNull()
    {
        var result = _matcher.Match("foo", new List<Snippet>());
        Assert.Null(result);
    }

    [Fact]
    public void Match_PunctuationStrippedBeforeComparison()
    {
        var snippets = new List<Snippet>
        {
            new() { Trigger = "hello world", Expansion = "greeting" },
        };

        var result = _matcher.Match("hello, world!", snippets);

        Assert.NotNull(result);
        Assert.Equal("greeting", result.Expansion);
    }
}
