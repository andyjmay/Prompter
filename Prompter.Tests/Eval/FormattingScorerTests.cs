using Xunit;

namespace Prompter.Tests.Eval;

public class FormattingScorerTests
{
    [Fact]
    public void Score_MissingPunctuation_IsLowerThanPreservingPunctuation()
    {
        string expected = "Hello, world. How are you?";
        string withPunct = "Hello, world. How are you?";
        string withoutPunct = "Hello world How are you";

        double scoreWith = Prompter.Eval.Scoring.FormattingScorer.Score(withPunct, expected);
        double scoreWithout = Prompter.Eval.Scoring.FormattingScorer.Score(withoutPunct, expected);

        Assert.True(scoreWithout < scoreWith, $"Missing punctuation should score lower. With: {scoreWith}, Without: {scoreWithout}");
    }

    [Fact]
    public void Score_IsBoundedBetweenZeroAndOne()
    {
        double score = Prompter.Eval.Scoring.FormattingScorer.Score("any text here", "expected output");
        Assert.InRange(score, 0.0, 1.0);
    }

    [Fact]
    public void Score_ExactMatch_ReturnsOne()
    {
        string text = "The quick brown fox.";
        double score = Prompter.Eval.Scoring.FormattingScorer.Score(text, text);
        Assert.Equal(1.0, score, precision: 3);
    }

    [Fact]
    public void Score_CaseMismatch_IsLowerThanExactCaseMatch()
    {
        string expected = "Hello World";
        string exactCase = "Hello World";
        string wrongCase = "hello world";

        double scoreExact = Prompter.Eval.Scoring.FormattingScorer.Score(exactCase, expected);
        double scoreWrong = Prompter.Eval.Scoring.FormattingScorer.Score(wrongCase, expected);

        Assert.True(scoreWrong < scoreExact, $"Wrong case should score lower. Exact: {scoreExact}, Wrong: {scoreWrong}");
    }
}
