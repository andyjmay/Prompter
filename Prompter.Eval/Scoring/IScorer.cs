using Prompter.Eval.Dataset;

namespace Prompter.Eval.Scoring;

public interface IScorer
{
    EvalScores Score(string rawText, string? formattedText, string finalText, EvalCase testCase, string modeId);
}
