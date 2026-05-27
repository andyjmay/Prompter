using Prompter.Services;

namespace Prompter.Eval.Scoring;

public static class FormattingScorer
{
    public static double Score(string actual, string expected)
    {
        double wordScore = WordF1Scorer.Score(actual, expected);
        double punctScore = ComputePunctuationScore(actual, expected);
        double caseScore = ComputeCaseScore(actual, expected);
        return 0.70 * wordScore + 0.15 * punctScore + 0.15 * caseScore;
    }

    private static double ComputePunctuationScore(string actual, string expected)
    {
        var actualPunct = actual.Where(char.IsPunctuation).ToList();
        var expectedPunct = expected.Where(char.IsPunctuation).ToList();

        if (expectedPunct.Count == 0 && actualPunct.Count == 0)
        {
            return 1.0;
        }

        var actualCounts = actualPunct.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        var expectedCounts = expectedPunct.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

        int overlap = 0;
        foreach (var kvp in expectedCounts)
        {
            if (actualCounts.TryGetValue(kvp.Key, out int actualCount))
            {
                overlap += Math.Min(kvp.Value, actualCount);
            }
        }

        int maxCount = Math.Max(expectedPunct.Count, actualPunct.Count);
        return maxCount == 0 ? 1.0 : (double)overlap / maxCount;
    }

    private static double ComputeCaseScore(string actual, string expected)
    {
        int actualUpper = actual.Count(char.IsUpper);
        int expectedUpper = expected.Count(char.IsUpper);
        int actualLower = actual.Count(char.IsLower);
        int expectedLower = expected.Count(char.IsLower);

        int upperOverlap = Math.Min(actualUpper, expectedUpper);
        int lowerOverlap = Math.Min(actualLower, expectedLower);

        int totalOverlap = upperOverlap + lowerOverlap;
        int maxTotal = Math.Max(actualUpper + actualLower, expectedUpper + expectedLower);

        return maxTotal == 0 ? 1.0 : (double)totalOverlap / maxTotal;
    }
}
