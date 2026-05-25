namespace Prompter.Models;

public static class ModeDefaults
{
    public const string StandardId = "standard";
    public const string FormalId = "formal";
    public const string RawId = "raw";
    public const string DebugId = "debug";
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

    public static IReadOnlyList<ModeConfig> AllBuiltIns => new[] { Standard, Formal, Raw, Debug };

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
