using Xunit;

namespace Prompter.Tests.Services;

public class WordF1ScorerTests
{
    [Theory]
    [InlineData("a a a", "a", 1.0, 1.0, 1.0)]
    [InlineData("a b", "a c", 0.5, 0.5, 0.5)]
    [InlineData("the quick brown fox", "the quick brown fox", 1.0, 1.0, 1.0)]
    public void ScoreDetailed_ReturnsExpectedValues(string actual, string expected, double expectedRecall, double expectedPrecision, double expectedF1)
    {
        var (recall, precision, f1) = Prompter.Services.WordF1Scorer.ScoreDetailed(actual, expected);

        Assert.Equal(expectedRecall, recall, precision: 3);
        Assert.Equal(expectedPrecision, precision, precision: 3);
        Assert.Equal(expectedF1, f1, precision: 3);
    }

    [Fact]
    public void Score_Duplicates_DoesNotExceedOne()
    {
        double score = Prompter.Services.WordF1Scorer.Score("a a a", "a");
        Assert.InRange(score, 0.0, 1.0);
        Assert.Equal(1.0, score, precision: 3);
    }

    [Fact]
    public void Score_EmptyActual_ReturnsZero()
    {
        double score = Prompter.Services.WordF1Scorer.Score("", "hello");
        Assert.Equal(0.0, score, precision: 3);
    }

    [Fact]
    public void Score_BothEmpty_ReturnsOne()
    {
        double score = Prompter.Services.WordF1Scorer.Score("", "");
        Assert.Equal(1.0, score, precision: 3);
    }

    [Fact]
    public void Score_MismatchedLengths_ReturnsBoundedValue()
    {
        double score = Prompter.Services.WordF1Scorer.Score("one two three four five", "one");
        Assert.InRange(score, 0.0, 1.0);
    }
}
