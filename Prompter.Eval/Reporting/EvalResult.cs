using Prompter.Eval.Dataset;
using Prompter.Eval.Scoring;
using Prompter.Services;

namespace Prompter.Eval.Reporting;

public record EvalResult(
    string ConfigLabel,
    string WhisperModel,
    string? ChatModel,
    string ModeId,
    string CaseId,
    string CasePhrase,
    PipelineResult PipelineResult,
    EvalScores Scores,
    TimeSpan Duration);
