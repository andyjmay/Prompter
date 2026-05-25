namespace Prompter.Models;

public static class ModeDefaults
{
    public const string StandardId = "standard";
    public const string FormalId = "formal";
    public const string RawId = "raw";
    public const string DebugId = "debug";
    public const string CodeId = "code";
    public const string DefaultCleanPrompt = "Remove filler words such as um, uh, like, you know, I mean, sort of, and basically. Do not rephrase sentences. Preserve all substantive content.";

    public static readonly ModeConfig Standard = new()
    {
        Id = StandardId,
        Name = "Standard",
        SystemPrompt = "You are a spelling and punctuation corrector. You do not write, rewrite, or respond to text. You only fix typos, capitalization, and missing punctuation. Never change meaning. Never add trailing commentary or ellipsis.",
        SkipFormatting = false,
        ShowDiagnosticOutput = false,
        IsBuiltIn = true
    };

    public static readonly ModeConfig Formal = new()
    {
        Id = FormalId,
        Name = "Formal",
        SystemPrompt = "You are a spelling and punctuation corrector. Remove filler words and expand contractions. Do not rewrite sentences or change meaning. Do not add or remove content. Never add trailing commentary or ellipsis.",
        SkipFormatting = false,
        ShowDiagnosticOutput = false,
        IsBuiltIn = true
    };

    public static readonly ModeConfig Raw = new()
    {
        Id = RawId,
        Name = "Raw",
        SystemPrompt = "Return the text exactly as provided, with no changes.",
        SkipFormatting = true,
        ShowDiagnosticOutput = false,
        IsBuiltIn = true
    };

    public static readonly ModeConfig Debug = new()
    {
        Id = DebugId,
        Name = "Debug",
        SystemPrompt = "You are a spelling and punctuation corrector. You do not write, rewrite, or respond to text. You only fix typos, capitalization, and missing punctuation. Never change meaning. Never add trailing commentary or ellipsis.",
        SkipFormatting = false,
        ShowDiagnosticOutput = true,
        IsBuiltIn = true
    };

    public static readonly ModeConfig Code = new()
    {
        Id = CodeId,
        Name = "Code",
        SystemPrompt = "You are a spelling, punctuation, and developer syntax corrector. You optimize transcription and formatting for software development workflows.\nFollow these rules:\n1. Fix spelling, capitalization, and punctuation, but treat code identifiers (such as camelCase, PascalCase, snake_case, and kebab-case) as immutable.\n2. Do not insert spaces around dots in file paths (e.g., \"user.controller.ts\" must remain exact).\n3. Preserve all brackets, parentheses, braces, angle brackets, and quotes.\n4. Do not spell out code symbols or operators (e.g., write \"=>\", \"===\", \"!=\", \"&&\", \"||\" instead of spelling them out).\n5. Ensure CLI commands (e.g., \"git commit -m \\\"fix\\\"\", \"docker compose up -d\") remain verbatim and syntactically correct.\n6. Do not write, rewrite, explain, or respond to text. Do not add trailing commentary, notes, or ellipsis. Output ONLY the corrected text.",
        SkipFormatting = false,
        ShowDiagnosticOutput = false,
        IsBuiltIn = true
    };

    public static IReadOnlyList<ModeConfig> AllBuiltIns => new[] { Standard, Formal, Raw, Debug, Code };

    public static ModeConfig? GetById(string id)
    {
        return AllBuiltIns.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public static List<ModeConfig> EnsureBuiltInsPresent(List<ModeConfig> modes)
    {
        var result = new List<ModeConfig>(modes);
        var existingIds = new HashSet<string>(modes.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var builtIn in AllBuiltIns)
        {
            if (!existingIds.Contains(builtIn.Id))
            {
                result.Add(builtIn);
            }
        }

        return result;
    }
}
