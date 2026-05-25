namespace Prompter.Eval.Scoring;

public static class FormattingScorer
{
    public static double Score(string actual, string expected)
        => WordF1Scorer.Score(actual, expected);
}
