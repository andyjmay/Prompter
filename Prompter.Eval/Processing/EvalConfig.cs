namespace Prompter.Eval.Processing;

public record EvalConfig(
    string WhisperModelAlias,
    string? ChatModelAlias,
    string ModeId,
    bool UseCustomChat = false,
    string? CustomChatModelPath = null);
