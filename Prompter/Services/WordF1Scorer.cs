namespace Prompter.Services;

public static class WordF1Scorer
{
    public static double Score(string actual, string expected)
    {
        var (_, _, f1) = ScoreDetailed(actual, expected);
        return f1;
    }

    public static (double Recall, double Precision, double F1) ScoreDetailed(string actual, string expected)
    {
        var actualWords = NormalizeAndSplit(actual);
        var expectedWords = NormalizeAndSplit(expected);

        if (expectedWords.Count == 0)
        {
            double result = actualWords.Count == 0 ? 1.0 : 0.0;
            return (result, result, result);
        }

        var actualSet = new HashSet<string>(actualWords);
        var expectedSet = new HashSet<string>(expectedWords);
        int matches = actualSet.Count(w => expectedSet.Contains(w));

        double recall = expectedSet.Count > 0 ? (double)matches / expectedSet.Count : 0.0;
        double precision = actualSet.Count > 0 ? (double)matches / actualSet.Count : 0.0;

        if (precision + recall == 0)
        {
            return (0.0, 0.0, 0.0);
        }

        double f1 = 2.0 * (precision * recall) / (precision + recall);
        f1 = Math.Clamp(f1, 0.0, 1.0);
        recall = Math.Clamp(recall, 0.0, 1.0);
        precision = Math.Clamp(precision, 0.0, 1.0);

        return (recall, precision, f1);
    }

    private static List<string> NormalizeAndSplit(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var cleaned = new string(text
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray());

        return cleaned
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}
