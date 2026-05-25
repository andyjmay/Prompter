namespace Prompter.Eval.Scoring;

public record EvalScores(
    double TranscriptionAccuracy,
    double FormattingFidelity,
    double Overall);
