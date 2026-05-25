using Prompter.Services;
using Xunit;

namespace Prompter.Tests;

public class TextFormatterSafeguardTests
{
    #region RejectIfHallucinated

    [Fact]
    public void RejectIfHallucinated_EmptyResult_ReturnsRaw()
    {
        var raw = "Hello world";
        var result = TextFormatter.RejectIfHallucinated(raw, "");
        Assert.Equal(raw, result);
    }

    [Fact]
    public void RejectIfHallucinated_ResultMoreThanDoubleLength_ReturnsRaw()
    {
        var raw = "Short text";
        var result = TextFormatter.RejectIfHallucinated(raw, "Short text with a huge amount of extra words appended here");
        Assert.Equal(raw, result);
    }

    [Fact]
    public void RejectIfHallucinated_ZeroOverlap_ReturnsRaw()
    {
        var raw = "apple banana cherry";
        var result = TextFormatter.RejectIfHallucinated(raw, "dog cat mouse");
        Assert.Equal(raw, result);
    }

    [Fact]
    public void RejectIfHallucinated_LowPreservationRatio_ReturnsRaw()
    {
        var raw = "The quick brown fox jumps over the lazy dog";
        var result = TextFormatter.RejectIfHallucinated(raw, "A completely rewritten sentence with different words entirely");
        Assert.Equal(raw, result);
    }

    [Fact]
    public void RejectIfHallucinated_MarkdownListPatterns_ReturnsRaw()
    {
        var raw = "Buy milk and eggs";
        var result = TextFormatter.RejectIfHallucinated(raw, "1. Buy milk\n2. Buy eggs");
        Assert.Equal(raw, result);
    }

    [Fact]
    public void RejectIfHallucinated_SufficientOverlap_ReturnsResult()
    {
        var raw = "Hello world this is a test";
        var result = TextFormatter.RejectIfHallucinated(raw, "Hello world this is a test.");
        Assert.Equal("Hello world this is a test.", result);
    }

    [Fact]
    public void RejectIfHallucinated_BulletAsterisk_ReturnsRaw()
    {
        var raw = "Todo list";
        var result = TextFormatter.RejectIfHallucinated(raw, "* Todo list\n* Another item");
        Assert.Equal(raw, result);
    }

    [Fact]
    public void RejectIfHallucinated_HeadingPatterns_ReturnsRaw()
    {
        var raw = "Summary";
        var result = TextFormatter.RejectIfHallucinated(raw, "## Summary\n### Details");
        Assert.Equal(raw, result);
    }

    #endregion

    #region StripOutputWrappers

    [Fact]
    public void StripOutputWrappers_StripsDictatedTags()
    {
        var raw = "Hello";
        var text = "[DICTATED_TEXT_START]Hello[DICTATED_TEXT_END]";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void StripOutputWrappers_StripsPrefix_HereIsTheCleanedText()
    {
        var raw = "Hello world";
        var text = "Here is the cleaned text: Hello world";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StripOutputWrappers_StripsPrefix_CaseInsensitive()
    {
        var raw = "Hello world";
        var text = "HERE IS THE CLEANED TEXT: Hello world";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StripOutputWrappers_StripsTrailingEmptyLines()
    {
        var raw = "Hello world";
        var text = "Hello world\n\n   \n";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StripOutputWrappers_StripsTrailingSuffix_AnythingElse()
    {
        var raw = "Hello world";
        var text = "Hello world Is there anything else I can help you with?";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StripOutputWrappers_RemovesSurroundingQuotes()
    {
        var raw = "Hello world";
        var text = "\"Hello world\"";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StripOutputWrappers_RemovesSurroundingBackticks()
    {
        var raw = "Hello world";
        var text = "`Hello world`";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StripOutputWrappers_StripsTrailingEllipsis_IfRawDoesNotEndWithEllipsis()
    {
        var raw = "Hello world";
        var text = "Hello world...";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StripOutputWrappers_KeepsTrailingEllipsis_IfRawEndsWithEllipsis()
    {
        var raw = "Hello world...";
        var text = "Hello world...";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world...", result);
    }

    [Fact]
    public void StripOutputWrappers_StripsTrailingDashLine()
    {
        var raw = "Hello world";
        var text = "Hello world\n---";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StripOutputWrappers_StripsTrailingBackticksBlock()
    {
        var raw = "Hello world";
        var text = "Hello world\n```";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StripOutputWrappers_LeavesCleanTextUntouched()
    {
        var raw = "Hello world";
        var text = "Hello world";
        var result = TextFormatter.StripOutputWrappers(text, raw);
        Assert.Equal("Hello world", result);
    }

    #endregion

    #region StripTrailingArtifactsByRawAlignment

    [Fact]
    public void StripTrailingArtifacts_ShorterThanRaw_ReturnsUnchanged()
    {
        var raw = "The quick brown fox";
        var result = "The quick";
        var stripped = TextFormatter.StripTrailingArtifactsByRawAlignment(result, raw);
        Assert.Equal("The quick", stripped);
    }

    [Fact]
    public void StripTrailingArtifacts_NoArtifact_ReturnsUnchanged()
    {
        var raw = "Hello world";
        var result = "Hello world";
        var stripped = TextFormatter.StripTrailingArtifactsByRawAlignment(result, raw);
        Assert.Equal("Hello world", stripped);
    }

    [Fact]
    public void StripTrailingArtifacts_LeavesLegitimateExtraWords()
    {
        // If trailing words don't look like artifacts, they are preserved
        var raw = "Hello world";
        var result = "Hello world and universe";
        var stripped = TextFormatter.StripTrailingArtifactsByRawAlignment(result, raw);
        Assert.Equal("Hello world and universe", stripped);
    }

    #endregion

    #region StripFillers

    [Fact]
    public void StripFillers_RemovesSingleFiller()
    {
        var result = TextFormatter.StripFillers("So, um, we need", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("So, we need", result);
    }

    [Fact]
    public void StripFillers_RemovesPhrase()
    {
        var result = TextFormatter.StripFillers("You know, I think", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("I think", result);
    }

    [Fact]
    public void StripFillers_PreservesDictionaryWord()
    {
        var protectedWords = new HashSet<string>(new[] { "like" }, StringComparer.OrdinalIgnoreCase);
        var result = TextFormatter.StripFillers("I like pizza", protectedWords);
        Assert.Equal("I like pizza", result);
    }

    [Fact]
    public void StripFillers_PreservesIntegralUsage()
    {
        var result = TextFormatter.StripFillers("I like pizza", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("I like pizza", result);
    }

    [Fact]
    public void StripFillers_CleansArtifacts()
    {
        var result = TextFormatter.StripFillers("So, um, we need to finalize the budget.", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("So, we need to finalize the budget.", result);
    }

    [Fact]
    public void StripFillers_RemovesContextualFiller_WhenFollowedByComma()
    {
        var result = TextFormatter.StripFillers("Like, I was saying", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("I was saying", result);
    }

    [Fact]
    public void StripFillers_RemovesContextualFiller_WhenSurroundedByCommas()
    {
        var result = TextFormatter.StripFillers("the, like, main issue", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("the, main issue", result);
    }

    #endregion

    #region RejectIfHallucinated_CleanMode

    [Fact]
    public void RejectIfHallucinated_CleanMode_HighFiller_AllowsResult()
    {
        var raw = "I, um, like, you know, I mean, I really, uh, like this.";
        var result = "I really like this.";
        var accepted = TextFormatter.RejectIfHallucinated(raw, result, cleanEnabled: true);
        Assert.Equal(result, accepted);
    }

    [Fact]
    public void RejectIfHallucinated_CleanMode_LowOverlap_Rejects()
    {
        var raw = "um uh like";
        var result = "completely different text";
        var rejected = TextFormatter.RejectIfHallucinated(raw, result, cleanEnabled: true);
        Assert.Equal(raw, rejected);
    }

    #endregion
}
