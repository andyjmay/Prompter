using Prompter.Models;
using Xunit;

namespace Prompter.Tests;

public class ModeDefaultsTests
{
    [Theory]
    [InlineData("standard")]
    [InlineData("formal")]
    [InlineData("raw")]
    [InlineData("debug")]
    [InlineData("code")]
    public void GetById_ExistingId_ReturnsMode(string id)
    {
        var mode = ModeDefaults.GetById(id);
        Assert.NotNull(mode);
        Assert.Equal(id, mode.Id, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetById_NonExistingId_ReturnsNull()
    {
        Assert.Null(ModeDefaults.GetById("nonexistent"));
    }

    [Fact]
    public void GetById_IsCaseInsensitive()
    {
        var mode = ModeDefaults.GetById("STANDARD");
        Assert.NotNull(mode);
        Assert.Equal("standard", mode.Id);
    }

    [Fact]
    public void EnsureBuiltInsPresent_AddsMissingBuiltIns()
    {
        var modes = new List<ModeConfig>
        {
            new() { Id = "custom", Name = "Custom", SystemPrompt = "test" }
        };

        var result = ModeDefaults.EnsureBuiltInsPresent(modes);

        Assert.Equal(6, result.Count);
        Assert.Contains(result, m => m.Id == "custom");
        Assert.Contains(result, m => m.Id == ModeDefaults.StandardId);
        Assert.Contains(result, m => m.Id == ModeDefaults.FormalId);
        Assert.Contains(result, m => m.Id == ModeDefaults.RawId);
        Assert.Contains(result, m => m.Id == ModeDefaults.DebugId);
        Assert.Contains(result, m => m.Id == ModeDefaults.CodeId);
    }

    [Fact]
    public void EnsureBuiltInsPresent_PreservesExistingCustomModes()
    {
        var custom = new ModeConfig { Id = "standard", Name = "My Standard", SystemPrompt = "custom prompt" };
        var modes = new List<ModeConfig> { custom };

        var result = ModeDefaults.EnsureBuiltInsPresent(modes);

        var standard = result.First(m => m.Id == "standard");
        Assert.Equal("My Standard", standard.Name);
        Assert.Equal("custom prompt", standard.SystemPrompt);
    }

    [Fact]
    public void EnsureBuiltInsPresent_DoesNotDuplicate()
    {
        var modes = new List<ModeConfig>(ModeDefaults.AllBuiltIns);
        var result = ModeDefaults.EnsureBuiltInsPresent(modes);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void AllBuiltIns_ContainsFiveModes()
    {
        Assert.Equal(5, ModeDefaults.AllBuiltIns.Count);
    }

    [Fact]
    public void StandardMode_HasCorrectDefaults()
    {
        var mode = ModeDefaults.Standard;
        Assert.Equal("standard", mode.Id);
        Assert.False(mode.SkipFormatting);
        Assert.False(mode.ShowDiagnosticOutput);
        Assert.True(mode.IsBuiltIn);
    }

    [Fact]
    public void RawMode_SkipsFormatting()
    {
        var mode = ModeDefaults.Raw;
        Assert.True(mode.SkipFormatting);
    }

    [Fact]
    public void DebugMode_ShowsDiagnosticOutput()
    {
        var mode = ModeDefaults.Debug;
        Assert.True(mode.ShowDiagnosticOutput);
    }
}
