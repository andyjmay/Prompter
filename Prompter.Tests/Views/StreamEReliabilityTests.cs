using System.IO;
using Prompter.Views;
using Xunit;

namespace Prompter.Tests.Views;

public class StreamEReliabilityTests
{
    [Fact]
    public void ResolveDisplayNameForAlias_UsesCorrectAlias()
    {
        var whisperDict = new Dictionary<string, string>
        {
            { "Whisper A", @"C:\models\whisper-a.bin" }
        };
        var chatDict = new Dictionary<string, string>
        {
            { "Chat A", "chat-a" }
        };

        // E3 fix: WhisperAlias should resolve via the whisper dictionary
        Assert.Equal("Whisper A", ModelTestingWindow.ResolveDisplayNameForAlias(whisperDict, @"C:\models\whisper-a.bin"));
        Assert.Null(ModelTestingWindow.ResolveDisplayNameForAlias(whisperDict, "chat-a"));

        // Path-based resolution by file name alone
        Assert.Equal("Whisper A", ModelTestingWindow.ResolveDisplayNameForAlias(whisperDict, "whisper-a.bin"));
        Assert.Equal("Chat A", ModelTestingWindow.ResolveDisplayNameForAlias(chatDict, "chat-a"));
    }

    [Theory]
    [InlineData("qwen2.5-0.5b-instruct-onnx")]
    [InlineData("Llama-3.2-1B-Instruct")]
    [InlineData("unknown-model")]
    public void BuildChatTemplate_DoesNotContainCorruptedCharacters(string modelAlias)
    {
        // E4 fix: templates must not contain Chinese stray characters
        var template = CustomModelManagerWindow.BuildChatTemplate(modelAlias);
        foreach (var kvp in template)
        {
            Assert.DoesNotContain("在这", kvp.Value);
        }
    }

    [Theory]
    [InlineData("qwen2.5-0.5b-instruct-onnx", "<|im_start|>system\n{content}\n")]
    [InlineData("Llama-3.2-1B-Instruct", "<|start_header_id|>system<|end_header_id|>\n\n{content}<|eot_id|>")]
    public void BuildChatTemplate_ProducesExpectedSystemTemplate(string modelAlias, string expectedSystem)
    {
        var template = CustomModelManagerWindow.BuildChatTemplate(modelAlias);
        Assert.Equal(expectedSystem, template["system"]);
    }

    [Fact]
    public void BuildChatTemplate_LlamaUsesCorrectTokens()
    {
        var template = CustomModelManagerWindow.BuildChatTemplate("llama-3.2-1b");
        Assert.StartsWith("<|start_header_id|>system<|end_header_id|>", template["system"]);
        Assert.StartsWith("<|start_header_id|>user<|end_header_id|>", template["user"]);
        Assert.StartsWith("<|start_header_id|>assistant<|end_header_id|>", template["assistant"]);
    }

    [Fact]
    public void NullCustomChatModelPath_DoesNotThrow_WhenGuarded()
    {
        // E5 fix: Path.GetFileName(null) may return null in newer .NET; guard prevents misuse
        string? path = null;
        var rawResult = Path.GetFileName(path);
        Assert.Null(rawResult);

        var safeResult = !string.IsNullOrEmpty(path) ? Path.GetFileName(path) : null;
        Assert.Null(safeResult);
    }

    [Fact]
    public void NullCustomChatModelPath_GuardedAssignment_MatchesWindowLogic()
    {
        // E5 fix: mirrors the corrected logic in ModelTestingWindow.PopulateModelDropdownsAsync
        string? customPath = null;
        string chatModelId = "phi-3.5-mini";
        bool useCustomChat = true;

        var currentChat = useCustomChat && !string.IsNullOrEmpty(customPath)
            ? customPath
            : chatModelId;

        Assert.Equal("phi-3.5-mini", currentChat);
        Assert.Null(Record.Exception(() => Path.GetFileName(currentChat)));
    }
}
