using Prompter.Eval.Dataset;
using Prompter.Models;

namespace Prompter.Eval.Scoring;

public class CompositeScorer : IScorer
{
    public EvalScores Score(string rawText, string? formattedText, string finalText, EvalCase testCase, string modeId)
    {
        double transcriptionScore = TranscriptionScorer.Score(rawText, testCase.ExpectedRawText);

        if (!testCase.ExpectedOutputByModeId.TryGetValue(modeId, out var expectedOutput))
        {
            if (modeId.Equals(ModeDefaults.CodeId, StringComparison.OrdinalIgnoreCase) && testCase.ExpectedOutputByModeId.TryGetValue(ModeDefaults.StandardId, out var standardOutput))
            {
                expectedOutput = standardOutput;
            }
            else
            {
                expectedOutput = testCase.ExpectedRawText;
            }
        }

        double formattingScore = FormattingScorer.Score(formattedText ?? finalText, expectedOutput);

        double overall = (transcriptionScore + formattingScore) / 2.0;

        return new EvalScores(transcriptionScore, formattingScore, overall);
    }
}
