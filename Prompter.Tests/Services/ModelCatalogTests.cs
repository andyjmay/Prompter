using Prompter.Models;
using Xunit;

namespace Prompter.Tests;

public class ModelCatalogTests
{
    [Theory]
    [InlineData("whisper-tiny", "~75 MB")]
    [InlineData("phi-3.5-mini", "~2.2 GB")]
    [InlineData("qwen2.5-0.5b-instruct", "~0.5 GB")]
    [InlineData("unknown-model", "Unknown")]
    public void GetSizeDescription_ReturnsExpected(string alias, string expected)
    {
        Assert.Equal(expected, ModelCatalog.GetSizeDescription(alias));
    }

    [Theory]
    [InlineData("whisper-tiny", 75f)]
    [InlineData("phi-3.5-mini", 2200f)]
    [InlineData("gemma-3-1b-it", 600f)]
    [InlineData("unknown-model", null)]
    public void GetSizeInMegabytes_ReturnsExpected(string alias, float? expected)
    {
        Assert.Equal(expected, ModelCatalog.GetSizeInMegabytes(alias));
    }

    [Theory]
    [InlineData("whisper-tiny", "Speech Transcription")]
    [InlineData("whisper-small-en", "Speech Transcription")]
    [InlineData("phi-3.5-mini", "Text Correction")]
    [InlineData("unknown-chat-model", "Text Correction")]
    public void GetTaskType_ReturnsExpected(string alias, string expected)
    {
        Assert.Equal(expected, ModelCatalog.GetTaskType(alias));
    }

    [Fact]
    public void Metadata_ContainsExpectedEntries()
    {
        Assert.Contains(ModelCatalog.Metadata, kvp => kvp.Key.Equals("whisper-tiny", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ModelCatalog.Metadata, kvp => kvp.Key.Equals("phi-3.5-mini", StringComparison.OrdinalIgnoreCase));
    }
}
