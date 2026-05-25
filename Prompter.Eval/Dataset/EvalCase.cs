namespace Prompter.Eval.Dataset;

public record EvalCase(
    string Id,
    string Phrase,
    string[] Tags,
    string ExpectedRawText,
    Dictionary<string, string> ExpectedOutputByModeId);
