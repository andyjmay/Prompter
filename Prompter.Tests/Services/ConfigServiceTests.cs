using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prompter.Models;
using Prompter.Services;
using Xunit;

namespace Prompter.Tests;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _service;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new ConfigService(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void Load_NoExistingFile_CreatesDefaultConfig()
    {
        var config = _service.Load();

        Assert.Equal(12, config.Version);
        Assert.Equal(ModeDefaults.StandardId, config.DefaultModeId);
        Assert.NotEmpty(config.Modes);
        Assert.True(config.UseClipboardPaste);
        Assert.Equal(150, config.PasteThresholdCharacters);
        Assert.True(File.Exists(Path.Combine(_tempDir, "config.json")));
    }

    [Fact]
    public void IsFirstRun_NoFile_ReturnsTrue()
    {
        Assert.True(_service.IsFirstRun());
    }

    [Fact]
    public void IsFirstRun_AfterLoad_ReturnsFalse()
    {
        _service.Load();
        Assert.False(_service.IsFirstRun());
    }

    [Fact]
    public async Task SaveAsync_RoundTripsThroughLoad()
    {
        var config = new AppConfig
        {
            Version = 11,
            HotkeyKey = "F10",
            SpokenPunctuationEnabled = true,
            DictionaryEntries = new() { new DictionaryEntry { Word = "Foo", Aliases = new() { "foo" } } }
        };

        await _service.SaveAsync(config);
        var loaded = _service.Load();

        Assert.Equal("F10", loaded.HotkeyKey);
        Assert.True(loaded.SpokenPunctuationEnabled);
        Assert.Single(loaded.DictionaryEntries);
    }

    [Fact]
    public void Load_V1Config_MigratesToV12()
    {
        var v1Json = """
        {
            "Version": 1,
            "DefaultMode": 1,
            "CustomSystemPrompt": "My custom prompt"
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v1Json);

        var config = _service.Load();

        Assert.Equal(12, config.Version);
        // CustomSystemPrompt triggers creation of a custom mode which becomes the default
        Assert.Equal("custom", config.DefaultModeId);
        var customMode = config.Modes.FirstOrDefault(m => m.Id == "custom");
        Assert.NotNull(customMode);
        Assert.Equal("My custom prompt", customMode.SystemPrompt);
    }

    [Fact]
    public void Load_V3Config_MigratesToV12()
    {
        var v3Json = """
        {
            "Version": 3,
            "DefaultMode": 2,
            "Modes": []
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v3Json);

        var config = _service.Load();

        Assert.Equal(12, config.Version);
        Assert.False(config.UseCustomWhisper);
        Assert.Equal("", config.CustomWhisperModelPath);
        // v4 migration reads the legacy numeric DefaultMode property
        Assert.Equal(ModeDefaults.RawId, config.DefaultModeId);
    }

    [Fact]
    public void Load_V4Config_MigratesToV12()
    {
        var v4Json = """
        {
            "Version": 4,
            "DefaultModeId": "standard",
            "Modes": [{"Id":"standard","Name":"Standard","SystemPrompt":"test"}]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v4Json);

        var config = _service.Load();

        Assert.Equal(12, config.Version);
        Assert.False(config.SpokenPunctuationEnabled);
    }

    [Fact]
    public void Load_V5Config_MigratesToV12()
    {
        var v5Json = """
        {
            "Version": 5,
            "SpokenPunctuationEnabled": true
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v5Json);

        var config = _service.Load();

        Assert.Equal(12, config.Version);
        Assert.True(config.SpokenPunctuationEnabled);
        Assert.NotNull(config.DictionaryEntries);
    }

    [Fact]
    public void Load_V6Config_MigratesToV12()
    {
        var v6Json = """
        {
            "Version": 6,
            "DictionaryEntries": [{"Word":"Test","Aliases":["test"]}]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v6Json);

        var config = _service.Load();

        Assert.Equal(12, config.Version);
        Assert.NotNull(config.Snippets);
        Assert.Single(config.DictionaryEntries);
    }

    [Fact]
    public void Load_V1WithNumericDefaultMode_MapsCorrectly()
    {
        var v1Json = """
        {
            "Version": 1,
            "DefaultMode": 2
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v1Json);

        var config = _service.Load();
        Assert.Equal(ModeDefaults.RawId, config.DefaultModeId);
    }

    [Fact]
    public void Load_V1WithUnknownNumericDefaultMode_FallsBackToStandard()
    {
        var v1Json = """
        {
            "Version": 1,
            "DefaultMode": 99
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v1Json);

        var config = _service.Load();
        Assert.Equal(ModeDefaults.StandardId, config.DefaultModeId);
    }

    [Fact]
    public void Load_V1WithEmptyCustomPrompt_DoesNotCreateCustomMode()
    {
        var v1Json = """
        {
            "Version": 1,
            "CustomSystemPrompt": "   "
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v1Json);

        var config = _service.Load();
        Assert.DoesNotContain(config.Modes, m => m.Id == "custom");
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "{ not valid json");
        var config = _service.Load();
        Assert.Equal(12, config.Version);
    }

    [Fact]
    public void Load_V1WithStringDefaultMode_PreservesString()
    {
        var v1Json = """
        {
            "Version": 1,
            "DefaultMode": "formal"
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v1Json);

        var config = _service.Load();
        Assert.Equal("formal", config.DefaultModeId);
    }

    [Fact]
    public void Load_V8ConfigWithCleanMode_MigratesToV9AndRemovesCleanMode()
    {
        var v8Json = """
        {
            "Version": 8,
            "DefaultModeId": "clean",
            "Modes": [
                {"Id":"standard","Name":"Standard","SystemPrompt":"test"},
                {"Id":"clean","Name":"Clean","SystemPrompt":"clean test"}
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v8Json);

        var config = _service.Load();

        Assert.Equal(12, config.Version);
        Assert.Equal(ModeDefaults.StandardId, config.DefaultModeId);
        Assert.DoesNotContain(config.Modes, m => m.Id == "clean");
    }

    [Fact]
    public void Load_EnsuresNestedObjectsNotNull()
    {
        var minimalJson = """
        {
            "Version": 12,
            "Modes": null
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), minimalJson);

        var config = _service.Load();
        Assert.NotNull(config.RecordingOverlay);
        Assert.NotNull(config.PreviewToast);
        Assert.NotNull(config.OverlayStyle);
        Assert.NotNull(config.DictionaryEntries);
        Assert.NotNull(config.Snippets);
        Assert.NotNull(config.Modes);
    }

    [Fact]
    public void Load_LowercaseVersion_DoesNotTriggerMigration()
    {
        var json = """
        {
            "version": 12,
            "SpokenPunctuationEnabled": true,
            "Modes": [{"Id":"standard","Name":"Standard","SystemPrompt":"test"}]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), json);

        var config = _service.Load();

        Assert.Equal(12, config.Version);
        Assert.True(config.SpokenPunctuationEnabled);
        Assert.Single(config.Modes);
    }

    [Fact]
    public async Task SaveAsync_AtomicWrite_DoesNotCorruptFile()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(async () =>
            {
                var cfg = new AppConfig { HotkeyKey = $"Key{idx}" };
                await _service.SaveAsync(cfg);
            }));
        }

        await Task.WhenAll(tasks);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, "config.json"));
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("HotkeyKey", out _));
    }

    [Fact]
    public void ConfigTypes_AllInitPropertiesHaveJsonPropertyName()
    {
        var types = new[]
        {
            typeof(AppConfig),
            typeof(ModeConfig),
            typeof(Snippet),
            typeof(DictionaryEntry),
            typeof(OverlayPlacementConfig),
            typeof(OverlayStyleConfig),
            typeof(PreviewToastSpecificConfig)
        };

        foreach (var type in types)
        {
            foreach (var prop in type.GetProperties())
            {
                if (prop.SetMethod is not null)
                {
                    var attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
                    Assert.NotNull(attr);
                    Assert.Equal(prop.Name, attr.Name);
                }
            }
        }
    }

    [Fact]
    public void Load_V10Config_MigratesToV11AndAddsCodeMode()
    {
        var v10Json = """
        {
            "Version": 10,
            "DefaultModeId": "standard",
            "Modes": [
                {"Id":"standard","Name":"Standard","SystemPrompt":"test"}
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), v10Json);

        var config = _service.Load();

        Assert.Equal(12, config.Version);
        Assert.Contains(config.Modes, m => m.Id == "code");
    }

    [Fact]
    public async Task PushTemporaryConfig_SetsOverride()
    {
        var original = new AppConfig { HotkeyKey = "F1" };
        await _service.SaveAsync(original);

        var temp = new AppConfig { HotkeyKey = "F2" };
        using (_service.PushTemporaryConfig(temp))
        {
            var loaded = _service.Load();
            Assert.Equal("F2", loaded.HotkeyKey);
        }
    }

    [Fact]
    public async Task PopTemporaryConfig_RestoresOriginal()
    {
        var original = new AppConfig { HotkeyKey = "F1" };
        await _service.SaveAsync(original);

        var temp = new AppConfig { HotkeyKey = "F2" };
        var scope = _service.PushTemporaryConfig(temp);
        scope.Dispose();

        var loaded = _service.Load();
        Assert.Equal("F1", loaded.HotkeyKey);
    }

    [Fact]
    public async Task NestedPushPop_WorksCorrectly()
    {
        var original = new AppConfig { HotkeyKey = "F1" };
        await _service.SaveAsync(original);

        var tempA = new AppConfig { HotkeyKey = "F2" };
        var tempB = new AppConfig { HotkeyKey = "F3" };

        using (_service.PushTemporaryConfig(tempA))
        {
            using (_service.PushTemporaryConfig(tempB))
            {
                Assert.Equal("F3", _service.Load().HotkeyKey);
            }
            Assert.Equal("F2", _service.Load().HotkeyKey);
        }
        Assert.Equal("F1", _service.Load().HotkeyKey);
    }

    [Fact]
    public void PopTemporaryConfig_EmptyStack_DoesNotThrow()
    {
        var method = typeof(ConfigService).GetMethod("PopTemporaryConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var ex = Record.Exception(() => method.Invoke(_service, null));
        Assert.Null(ex);
    }
}
