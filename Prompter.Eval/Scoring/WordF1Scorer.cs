namespace Prompter.Eval.Scoring;

public static class WordF1Scorer
{
    public static double Score(string actual, string expected)
    {
        var actualWords = NormalizeAndSplit(actual);
        var expectedWords = NormalizeAndSplit(expected);

        if (expectedWords.Count == 0)
        {
            return actualWords.Count == 0 ? 1.0 : 0.0;
        }

        var expectedSet = new HashSet<string>(expectedWords);
        int matches = actualWords.Count(w => expectedSet.Contains(w));

        double recall = (double)matches / expectedWords.Count;
        double precision = actualWords.Count > 0 ? (double)matches / actualWords.Count : 0.0;

        if (precision + recall == 0)
        {
            return 0.0;
        }

        return 2.0 * (precision * recall) / (precision + recall);
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
