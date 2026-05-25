namespace Prompter.Eval.Reporting;

public class ConsoleReporter : IReporter
{
    public void Report(IReadOnlyList<EvalResult> results)
    {
        if (results.Count == 0)
        {
            Console.WriteLine("No results to report.");
            return;
        }

        var ranked = results
            .OrderByDescending(r => r.Scores.Overall)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("=== EVALUATION RESULTS ===");
        Console.WriteLine();

        var groupedByConfig = ranked
            .GroupBy(r => r.ConfigLabel)
            .Select(g => new
            {
                ConfigLabel = g.Key,
                WhisperModel = g.First().WhisperModel,
                ChatModel = g.First().ChatModel,
                ModeId = g.First().ModeId,
                AvgOverall = g.Average(r => r.Scores.Overall),
                AvgTranscription = g.Average(r => r.Scores.TranscriptionAccuracy),
                AvgFormatting = g.Average(r => r.Scores.FormattingFidelity),
                AvgDuration = TimeSpan.FromMilliseconds(g.Average(r => r.Duration.TotalMilliseconds))
            })
            .OrderByDescending(x => x.AvgOverall)
            .ToList();

        Console.WriteLine("Ranked by average overall score (higher is better):");
        Console.WriteLine();
        Console.WriteLine($"{"Rank",-5} {"Config",-35} {"Transcription",-14} {"Formatting",-12} {"Overall",-10} {"Avg Time"}");
        Console.WriteLine(new string('-', 100));

        for (int i = 0; i < groupedByConfig.Count; i++)
        {
            var g = groupedByConfig[i];
            Console.WriteLine(
                $"{i + 1,-5} {g.ConfigLabel,-35} {g.AvgTranscription,14:F3} {g.AvgFormatting,12:F3} {g.AvgOverall,10:F3} {g.AvgDuration:ss\\.fff}s");
        }

        Console.WriteLine();
        Console.WriteLine("=== PER-CASE DETAIL ===");
        Console.WriteLine();

        foreach (var result in ranked)
        {
            Console.WriteLine(
                $"[{result.ConfigLabel}] {result.CaseId,-20} " +
                $"Transcription={result.Scores.TranscriptionAccuracy:F3} " +
                $"Formatting={result.Scores.FormattingFidelity:F3} " +
                $"Overall={result.Scores.Overall:F3} " +
                $"({result.Duration:ss\\.fff}s)");
            Console.WriteLine($"  Raw:    {result.PipelineResult.RawText}");
            Console.WriteLine($"  Final:  {result.PipelineResult.FinalText}");
            Console.WriteLine();
        }
    }
}
