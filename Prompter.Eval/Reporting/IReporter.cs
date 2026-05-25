namespace Prompter.Eval.Reporting;

public interface IReporter
{
    void Report(IReadOnlyList<EvalResult> results);
}
