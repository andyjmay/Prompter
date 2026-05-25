using Prompter.Models;
using Prompter.Services;
using Xunit;

namespace Prompter.Tests.Services;

public class InputInjectorServiceTests
{
    private readonly InputInjectorService _service = new(new FakeFileLogger());

    #region ValidateExpansion

    [Fact]
    public void ValidateExpansion_Empty_DoesNotThrow()
    {
        var ex = Record.Exception(() => _service.ValidateExpansion(""));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateExpansion_PlainText_DoesNotThrow()
    {
        var ex = Record.Exception(() => _service.ValidateExpansion("Hello world"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("{Enter}")]
    [InlineData("{TAB}")]
    [InlineData("{escape}")]
    [InlineData("{Backspace}")]
    [InlineData("{Delete}")]
    [InlineData("{Up}")]
    [InlineData("{Down}")]
    [InlineData("{Left}")]
    [InlineData("{Right}")]
    [InlineData("{Home}")]
    [InlineData("{End}")]
    [InlineData("{PageUp}")]
    [InlineData("{PageDown}")]
    [InlineData("{Ctrl+A}")]
    [InlineData("{Ctrl+C}")]
    [InlineData("{Ctrl+V}")]
    public void ValidateExpansion_ValidTokens_DoesNotThrow(string expansion)
    {
        var ex = Record.Exception(() => _service.ValidateExpansion(expansion));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateExpansion_UnknownToken_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateExpansion("{Foo}"));
        Assert.Contains("Foo", ex.Message);
    }

    [Fact]
    public void ValidateExpansion_UnmatchedOpenBrace_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateExpansion("{Enter"));
        Assert.Contains("Unmatched opening brace", ex.Message);
    }

    [Fact]
    public void ValidateExpansion_UnmatchedCloseBrace_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateExpansion("Enter}"));
        Assert.Contains("Unmatched closing brace", ex.Message);
    }

    [Fact]
    public void ValidateExpansion_EscapedBraces_DoesNotThrow()
    {
        var ex = Record.Exception(() => _service.ValidateExpansion("{{foo}}"));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateExpansion_MixedTextAndTokens_DoesNotThrow()
    {
        var ex = Record.Exception(() => _service.ValidateExpansion("Hello{Enter}World{Tab}End"));
        Assert.Null(ex);
    }

    #endregion

    #region ContainsKeyTokens

    [Fact]
    public void ContainsKeyTokens_PlainText_ReturnsFalse()
    {
        Assert.False(_service.ContainsKeyTokens("Hello world"));
    }

    [Fact]
    public void ContainsKeyTokens_WithEnter_ReturnsTrue()
    {
        Assert.True(_service.ContainsKeyTokens("Hello{Enter}World"));
    }

    [Fact]
    public void ContainsKeyTokens_WithCtrlV_ReturnsTrue()
    {
        Assert.True(_service.ContainsKeyTokens("{Ctrl+V}"));
    }

    [Fact]
    public void ContainsKeyTokens_EscapedBraces_ReturnsFalse()
    {
        Assert.False(_service.ContainsKeyTokens("{{Enter}}"));
    }

    [Fact]
    public void ContainsKeyTokens_UnknownToken_ReturnsFalse()
    {
        Assert.False(_service.ContainsKeyTokens("{Foo}"));
    }

    [Fact]
    public void ContainsKeyTokens_UnmatchedBrace_ReturnsFalse()
    {
        Assert.False(_service.ContainsKeyTokens("{Enter"));
    }

    #endregion

    private sealed class FakeFileLogger : IFileLogger
    {
        public void Log(string message) { }
        public void LogException(Exception ex, string context) { }
        public IEnumerable<LogEntry> GetRecentLogs(int maxEntries = 5000) => Array.Empty<LogEntry>();
        public void ClearLogs() { }
    }
}
