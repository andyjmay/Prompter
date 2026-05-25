using System.Text.RegularExpressions;

namespace Prompter.Services;

internal static class SpokenPunctuationProcessor
{
    private enum SpacingType
    {
        TrailingSpace,
        OpenQuote,
        CloseQuote,
        Structural
    }

    private readonly record struct TokenRule(string Token, string Replacement, SpacingType Spacing);

    private static readonly TokenRule[] Rules =
    [
        new("exclamation point", "!", SpacingType.TrailingSpace),
        new("exclamation mark", "!", SpacingType.TrailingSpace),
        new("question mark", "?", SpacingType.TrailingSpace),
        new("open quote", "\"", SpacingType.OpenQuote),
        new("close quote", "\"", SpacingType.CloseQuote),
        new("new paragraph", Environment.NewLine + Environment.NewLine, SpacingType.Structural),
        new("new line", Environment.NewLine, SpacingType.Structural),
        new("ellipsis", "...", SpacingType.TrailingSpace),
        new("semicolon", ";", SpacingType.TrailingSpace),
        new("period", ".", SpacingType.TrailingSpace),
        new("comma", ",", SpacingType.TrailingSpace),
        new("colon", ":", SpacingType.TrailingSpace),
        new("dash", "-", SpacingType.TrailingSpace),
        new("tab", "\t", SpacingType.Structural),
        new("at sign", "@", SpacingType.TrailingSpace),
        new("hashtag", "#", SpacingType.TrailingSpace),
    ];

    public static string Process(string input, string language, IFileLogger logger)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        if (!language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return input;

        var current = input;
        int totalReplacements = 0;
        var appliedTokens = new List<string>();

        foreach (var rule in Rules.OrderByDescending(r => r.Token.Length))
        {
            int count = 0;
            string pattern = rule.Spacing == SpacingType.Structural
                ? $@"\b(\s*)(?<!\b(?:a|an|the|this|that)\s+){Regex.Escape(rule.Token)}(?=\b)(\s*)"
                : $@"\b( ?)(?<!\b(?:a|an|the|this|that)\s+){Regex.Escape(rule.Token)}( ?)(?!\w)";

            current = Regex.Replace(current, pattern, m =>
            {
                string leading = m.Groups[1].Value;
                string trailing = m.Groups[2].Value;
                string replacement = rule.Replacement;

                switch (rule.Spacing)
                {
                    case SpacingType.TrailingSpace:
                        replacement += trailing;
                        break;
                    case SpacingType.OpenQuote:
                        replacement = leading + replacement;
                        break;
                    case SpacingType.CloseQuote:
                        replacement += trailing;
                        break;
                    case SpacingType.Structural:
                        // consume all captured whitespace, return only structural char
                        break;
                }

                count++;
                return replacement;
            }, RegexOptions.IgnoreCase);

            if (count > 0)
            {
                totalReplacements += count;
                appliedTokens.Add(rule.Token);
            }
        }

        if (totalReplacements > 0)
        {
            logger.Log($"SpokenPunctuation: {totalReplacements} replacement{(totalReplacements == 1 ? "" : "s")} applied ({string.Join(", ", appliedTokens)}).");
        }

        return current;
    }
}
