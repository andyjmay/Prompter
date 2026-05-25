using Prompter.Models;

namespace Prompter.Services;

public interface ISnippetMatcher
{
    Snippet? Match(string transcription, List<Snippet> snippets);
}
