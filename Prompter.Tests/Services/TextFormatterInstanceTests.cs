using Prompter.Models;
using Prompter.Services;
using Prompter.Tests.Fakes;
using Xunit;

namespace Prompter.Tests.Services;

public class TextFormatterInstanceTests
{
    private readonly FakeFileLogger _logger = new();

    #region Setup Helpers

    private static TextFormatter CreateFormatter(
        IModelManager modelManager,
        IConfigService configService,
        IFileLogger logger)
    {
        return new TextFormatter(modelManager, configService, logger);
    }

    private static FakeModelManager CreateManager(IChatClient chatClient, bool chatReady = true)
    {
        return new FakeModelManager(chatClient) { ChatReady = chatReady };
    }


    #endregion

    #region ChatNotReady

    [Fact]
    public async Task CleanupAsync_ChatNotReady_ThrowsInvalidOperationException()
    {
        var chatClient = new FakeChatClient("result");
        var manager = CreateManager(chatClient, chatReady: false);
        var configService = new FakeConfigService(new AppConfig());
        var formatter = CreateFormatter(manager, configService, _logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => formatter.CleanupAsync("hello world", "standard", CancellationToken.None));

        Assert.Equal("Chat model not loaded", ex.Message);
    }

    #endregion

    #region Standard Mode

    [Fact]
    public async Task CleanupAsync_StandardMode_ReturnsFormattedText()
    {
        var chatClient = new FakeChatClient("Hello world.");
        var manager = CreateManager(chatClient);
        var configService = new FakeConfigService(new AppConfig());
        var formatter = CreateFormatter(manager, configService, _logger);

        var result = await formatter.CleanupAsync("hello world", "standard", CancellationToken.None);

        Assert.Equal("Hello world.", result);
    }

    [Fact]
    public async Task CleanupAsync_EmptyChatResult_ReturnsRawText()
    {
        var chatClient = new FakeChatClient((string?)null);
        var manager = CreateManager(chatClient);
        var configService = new FakeConfigService(new AppConfig());
        var formatter = CreateFormatter(manager, configService, _logger);

        var result = await formatter.CleanupAsync("hello world", "standard", CancellationToken.None);

        Assert.Equal("hello world", result);
        Assert.Contains("null or empty response", _logger.LogBuilder.ToString());
    }

    #endregion

    #region Clean Mode

    [Fact]
    public async Task CleanupAsync_CleanEnabled_StripsFillersFromResult()
    {
        // Chat model returns text with fillers; safety-net StripFillers removes them
        var chatClient = new FakeChatClient("So, um, we need to finalize.");
        var manager = CreateManager(chatClient);
        var config = new AppConfig { CleanEnabled = true };
        var configService = new FakeConfigService(config);
        var formatter = CreateFormatter(manager, configService, _logger);

        var result = await formatter.CleanupAsync("we need to finalize", "standard", CancellationToken.None);

        // StripFillers removes "um" and collapses extra punctuation/spaces
        Assert.DoesNotContain("um", result);
    }

    [Fact]
    public async Task CleanupAsync_CleanEnabled_AppendsCleanInstructionToSystemPrompt()
    {
        string? capturedSystemPrompt = null;
        var chatClient = new FakeChatClient((messages, ct) =>
        {
            capturedSystemPrompt = messages.First(m => m.Role == "system").Content;
            return "formatted";
        });
        var manager = CreateManager(chatClient);
        var config = new AppConfig
        {
            CleanEnabled = true,
            CleanPrompt = "CUSTOM_CLEAN_INSTRUCTION"
        };
        var configService = new FakeConfigService(config);
        var formatter = CreateFormatter(manager, configService, _logger);

        await formatter.CleanupAsync("hello world", "standard", CancellationToken.None);

        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains("CUSTOM_CLEAN_INSTRUCTION", capturedSystemPrompt);
    }

    #endregion

    #region List Formatting

    [Fact]
    public async Task CleanupAsync_ListFormattingEnabled_AppendsListPromptToSystemPrompt()
    {
        string? capturedSystemPrompt = null;
        var chatClient = new FakeChatClient((messages, ct) =>
        {
            capturedSystemPrompt = messages.First(m => m.Role == "system").Content;
            return "1. Milk\n2. Eggs";
        });
        var manager = CreateManager(chatClient);
        var config = new AppConfig { ListFormattingEnabled = true };
        var configService = new FakeConfigService(config);
        var formatter = CreateFormatter(manager, configService, _logger);

        await formatter.CleanupAsync("grocery list 1. Milk 2. Eggs", "standard", CancellationToken.None);

        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains("markdown lists", capturedSystemPrompt);
    }

    [Fact]
    public async Task CleanupAsync_ListFormattingEnabled_AppliesListSpacingSafetyNet()
    {
        // Chat returns already formatted list; safety net normalizes spacing
        var chatClient = new FakeChatClient("Grocery list:\n\n1. Milk\n\n2. Eggs\n\nDone.");
        var manager = CreateManager(chatClient);
        var config = new AppConfig { ListFormattingEnabled = true };
        var configService = new FakeConfigService(config);
        var formatter = CreateFormatter(manager, configService, _logger);

        var result = await formatter.CleanupAsync("grocery list 1. Milk 2. Eggs Done.", "standard", CancellationToken.None);

        // FormatListSpacing removes excessive blank lines between list items
        Assert.Contains("1. Milk\n2. Eggs", result);
    }

    #endregion

    #region Dictionary Preservation

    [Fact]
    public async Task CleanupAsync_DictionaryWords_PreservesInSystemPrompt()
    {
        string? capturedSystemPrompt = null;
        var chatClient = new FakeChatClient((messages, ct) =>
        {
            capturedSystemPrompt = messages.First(m => m.Role == "system").Content;
            return "formatted";
        });
        var manager = CreateManager(chatClient);
        var config = new AppConfig
        {
            DictionaryEntries =
            {
                new DictionaryEntry { Word = "OpenAI", Aliases = new() { "open ai" } },
                new DictionaryEntry { Word = "GitHub", Aliases = new() { "github" } }
            }
        };
        var configService = new FakeConfigService(config);
        var formatter = CreateFormatter(manager, configService, _logger);

        await formatter.CleanupAsync("we use OpenAI and GitHub", "standard", CancellationToken.None);

        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains("Preserve the exact spelling", capturedSystemPrompt);
        Assert.Contains("OpenAI", capturedSystemPrompt);
        Assert.Contains("GitHub", capturedSystemPrompt);
    }

    [Fact]
    public async Task CleanupAsync_DictionaryWords_OnlyIncludesWordsPresentInRawText()
    {
        string? capturedSystemPrompt = null;
        var chatClient = new FakeChatClient((messages, ct) =>
        {
            capturedSystemPrompt = messages.First(m => m.Role == "system").Content;
            return "formatted";
        });
        var manager = CreateManager(chatClient);
        var config = new AppConfig
        {
            DictionaryEntries =
            {
                new DictionaryEntry { Word = "OpenAI", Aliases = new() { "open ai" } },
                new DictionaryEntry { Word = "Rust", Aliases = new() { "rust" } }
            }
        };
        var configService = new FakeConfigService(config);
        var formatter = CreateFormatter(manager, configService, _logger);

        await formatter.CleanupAsync("we love OpenAI", "standard", CancellationToken.None);

        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains("OpenAI", capturedSystemPrompt);
        Assert.DoesNotContain("Rust", capturedSystemPrompt);
    }

    #endregion

    #region Code Mode

    [Fact]
    public async Task CleanupAsync_CodeMode_AppliesCodeSafeguards()
    {
        var chatClient = new FakeChatClient("user dot controller dot ts");
        var manager = CreateManager(chatClient);
        var configService = new FakeConfigService(new AppConfig());
        var formatter = CreateFormatter(manager, configService, _logger);

        var result = await formatter.CleanupAsync("user dot controller dot ts", "code", CancellationToken.None);

        Assert.Equal("user.controller.ts", result);
    }

    [Fact]
    public async Task CleanupAsync_CodeMode_LowOverlapAllowed()
    {
        // Code mode allows lower preservation ratio (0.15 vs 0.4)
        var chatClient = new FakeChatClient("function userController()");
        var manager = CreateManager(chatClient);
        var configService = new FakeConfigService(new AppConfig());
        var formatter = CreateFormatter(manager, configService, _logger);

        var result = await formatter.CleanupAsync("create function named user controller", "code", CancellationToken.None);

        Assert.Equal("function userController()", result);
    }

    #endregion

    #region Fallback / Edge Cases

    [Fact]
    public async Task CleanupAsync_UnknownModeId_FallsBackToStandardPrompt()
    {
        string? capturedSystemPrompt = null;
        var chatClient = new FakeChatClient((messages, ct) =>
        {
            capturedSystemPrompt = messages.First(m => m.Role == "system").Content;
            return "formatted";
        });
        var manager = CreateManager(chatClient);
        var configService = new FakeConfigService(new AppConfig());
        var formatter = CreateFormatter(manager, configService, _logger);

        await formatter.CleanupAsync("hello world", "nonexistent-mode", CancellationToken.None);

        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains(ModeDefaults.Standard.SystemPrompt, capturedSystemPrompt);
    }

    [Fact]
    public async Task CleanupAsync_FlexibleFormattingUserMessage_HasExtraConstraints()
    {
        string? capturedUserMessage = null;
        var chatClient = new FakeChatClient((messages, ct) =>
        {
            capturedUserMessage = messages.First(m => m.Role == "user").Content;
            return "formatted";
        });
        var manager = CreateManager(chatClient);
        var config = new AppConfig { CleanEnabled = true };
        var configService = new FakeConfigService(config);
        var formatter = CreateFormatter(manager, configService, _logger);

        await formatter.CleanupAsync("hello world", "standard", CancellationToken.None);

        Assert.NotNull(capturedUserMessage);
        Assert.Contains("dictated speech", capturedUserMessage);
        Assert.Contains("Do NOT add, remove, or re-order sentences", capturedUserMessage);
    }

    [Fact]
    public async Task CleanupAsync_NonFlexibleFormattingUserMessage_HasCopyExactlyConstraint()
    {
        string? capturedUserMessage = null;
        var chatClient = new FakeChatClient((messages, ct) =>
        {
            capturedUserMessage = messages.First(m => m.Role == "user").Content;
            return "formatted";
        });
        var manager = CreateManager(chatClient);
        var configService = new FakeConfigService(new AppConfig()); // CleanEnabled = false
        var formatter = CreateFormatter(manager, configService, _logger);

        await formatter.CleanupAsync("hello world", "standard", CancellationToken.None);

        Assert.NotNull(capturedUserMessage);
        Assert.Contains("Copy it exactly", capturedUserMessage);
    }

    #endregion
}
