namespace Prompter.Eval.Dataset;

public static class DatasetLoader
{
    public static EvalDataset LoadDefault()
    {
        var cases = new List<EvalCase>
        {
            new(
                "basic-greeting",
                "Hello world, this is a test.",
                new[] { "short", "punctuation" },
                "hello world this is a test",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "hello world this is a test",
                    ["standard"] = "Hello world, this is a test.",
                    ["formal"] = "Hello world, this is a test."
                }
            ),
            new(
                "with-contractions",
                "I can't believe it's working so well.",
                new[] { "contractions" },
                "i can't believe it's working so well",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "i can't believe it's working so well",
                    ["standard"] = "I can't believe it's working so well.",
                    ["formal"] = "I cannot believe it is working so well."
                }
            ),
            new(
                "with-numbers",
                "The total is forty two dollars and zero cents.",
                new[] { "numbers" },
                "the total is forty two dollars and zero cents",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "the total is forty two dollars and zero cents",
                    ["standard"] = "The total is forty-two dollars and zero cents.",
                    ["formal"] = "The total is forty-two dollars and zero cents."
                }
            ),
            new(
                "with-filler",
                "Um, like, please confirm the meeting at three P M, thank you.",
                new[] { "filler", "formal-stress" },
                "um like please confirm the meeting at three p m thank you",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "um like please confirm the meeting at three p m thank you",
                    ["standard"] = "Please confirm the meeting at 3 PM. Thank you.",
                    ["formal"] = "Please confirm the meeting at 3:00 PM. Thank you."
                }
            ),
            new(
                "multi-sentence",
                "The project deadline is next Friday. We need to finish the documentation and submit the report. Please confirm your availability.",
                new[] { "multi-sentence", "punctuation" },
                "the project deadline is next friday we need to finish the documentation and submit the report please confirm your availability",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "the project deadline is next friday we need to finish the documentation and submit the report please confirm your availability",
                    ["standard"] = "The project deadline is next Friday. We need to finish the documentation and submit the report. Please confirm your availability.",
                    ["formal"] = "The project deadline is next Friday. We must finish the documentation and submit the report. Please confirm your availability."
                }
            ),
            new(
                "question-text",
                "Should we meet at two P M or three P M in the conference room?",
                new[] { "question", "numbers" },
                "should we meet at two p m or three p m in the conference room",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "should we meet at two p m or three p m in the conference room",
                    ["standard"] = "Should we meet at 2 PM or 3 PM in the conference room?",
                    ["formal"] = "Should we meet at 2:00 PM or 3:00 PM in the conference room?"
                }
            ),
            new(
                "acronym-heavy",
                "The FBI and CIA work with NASA and the EPA on AI research.",
                new[] { "acronyms", "capitalization" },
                "the fbi and cia work with nasa and the epa on ai research",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "the fbi and cia work with nasa and the epa on ai research",
                    ["standard"] = "The FBI and CIA work with NASA and the EPA on AI research.",
                    ["formal"] = "The FBI and CIA work with NASA and the EPA on AI research."
                }
            ),
            new(
                "proper-nouns",
                "Did Sarah Johnson from McKinsey visit Salesforce yesterday?",
                new[] { "proper-nouns", "capitalization" },
                "did sarah johnson from mckinsey visit salesforce yesterday",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "did sarah johnson from mckinsey visit salesforce yesterday",
                    ["standard"] = "Did Sarah Johnson from McKinsey visit Salesforce yesterday?",
                    ["formal"] = "Did Sarah Johnson from McKinsey visit Salesforce yesterday?"
                }
            ),
            new(
                "mixed-punctuation",
                "First, review the document; then, send your feedback. However, if it's urgent, call me immediately!",
                new[] { "punctuation", "complex" },
                "first review the document then send your feedback however if it's urgent call me immediately",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "first review the document then send your feedback however if it's urgent call me immediately",
                    ["standard"] = "First, review the document; then, send your feedback. However, if it's urgent, call me immediately!",
                    ["formal"] = "First, review the document; then, send your feedback. However, if it is urgent, call me immediately!"
                }
            ),
            new(
                "no-answer-stress",
                "What is the best way to format text? I think it's using a good model.",
                new[] { "no-answer", "hallucination-stress" },
                "what is the best way to format text i think it's using a good model",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "what is the best way to format text i think it's using a good model",
                    ["standard"] = "What is the best way to format text? I think it's using a good model.",
                    ["formal"] = "What is the best way to format text? I think it is using a good model."
                }
            ),
            new(
                "spoken-punctuation-words",
                "Type period at the end of the sentence and comma where needed.",
                new[] { "spoken-punctuation-words" },
                "type period at the end of the sentence and comma where needed",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "type period at the end of the sentence and comma where needed",
                    ["standard"] = "Type period at the end of the sentence and comma where needed.",
                    ["formal"] = "Type period at the end of the sentence and comma where needed."
                }
            ),
            new(
                "long-form",
                "Good morning team. I wanted to follow up on our discussion from yesterday. We agreed that the marketing team would handle the social media campaign while engineering focuses on the release. Please let me know if anything has changed. Looking forward to our next meeting.",
                new[] { "long-form", "multi-sentence" },
                "good morning team i wanted to follow up on our discussion from yesterday we agreed that the marketing team would handle the social media campaign while engineering focuses on the release please let me know if anything has changed looking forward to our next meeting",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "good morning team i wanted to follow up on our discussion from yesterday we agreed that the marketing team would handle the social media campaign while engineering focuses on the release please let me know if anything has changed looking forward to our next meeting",
                    ["standard"] = "Good morning team. I wanted to follow up on our discussion from yesterday. We agreed that the marketing team would handle the social media campaign while engineering focuses on the release. Please let me know if anything has changed. Looking forward to our next meeting.",
                    ["formal"] = "Good morning team. I wanted to follow up on our discussion from yesterday. We agreed that the marketing team would handle the social media campaign while engineering focuses on the release. Please inform me if anything has changed. I look forward to our next meeting."
                }
            ),
            new(
                "code-import-file-path",
                "import { userController } from './utils'",
                new[] { "code", "file-path" },
                "import open brace user controller close brace from quote dot slash utils quote",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "import open brace user controller close brace from quote dot slash utils quote",
                    ["standard"] = "Import open brace user controller close brace from quote dot slash utils quote.",
                    ["formal"] = "Import open brace user controller close brace from quote dot slash utils quote.",
                    ["code"] = "import { userController } from './utils'"
                }
            ),
            new(
                "code-cli-command",
                "git commit -m \"fix null pointer\"",
                new[] { "code", "cli" },
                "git commit dash m fix null pointer",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["raw"] = "git commit dash m fix null pointer",
                    ["standard"] = "Git commit dash m fix null pointer.",
                    ["formal"] = "Git commit dash m fix null pointer.",
                    ["code"] = "git commit -m \"fix null pointer\""
                }
            )
        };

        return new EvalDataset(cases);
    }
}
