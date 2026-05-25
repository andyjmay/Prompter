using Prompter.Services;

namespace Prompter.Eval.Scoring;

public static class TranscriptionScorer
{
    public static double Score(string actual, string expected)
        => WordF1Scorer.Score(actual, expected);
}
