using System.IO;
using System.Text.Json;
using Prompter.Models;

namespace Prompter.Services;

public class ConfigService : IConfigService
{
    private readonly string _configDir;
    private readonly string _configPath;
    private AppConfig? _cached;
    private readonly object _cacheLock = new();
    private readonly IFileLogger? _logger;

    public event EventHandler<AppConfig>? ConfigChanged;

    public ConfigService(IFileLogger? logger = null)
    {
        _logger = logger;
        _configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prompter");
        _configPath = Path.Combine(_configDir, "config.json");
    }

    internal ConfigService(string configDir, IFileLogger? logger = null)
    {
        _logger = logger;
        _configDir = configDir;
        _configPath = Path.Combine(_configDir, "config.json");
    }

    public AppConfig Load()
    {
        lock (_cacheLock)
        {
            if (_cached is not null) return _cached;

            if (!File.Exists(_configPath))
            {
                _cached = new AppConfig();
                Directory.CreateDirectory(_configDir);
                SaveToDisk(_cached);
                return _cached;
            }

            string jsonText;
            JsonDocument doc;
            try
            {
                jsonText = File.ReadAllText(_configPath);
                doc = JsonDocument.Parse(jsonText);
            }
            catch (Exception ex)
            {
                _logger?.LogException(ex, "ConfigService.Load - corrupt config reset to defaults");
                _cached = new AppConfig();
                SaveToDisk(_cached);
                return _cached;
            }

            using (doc)
            {
                var deserialized = JsonSerializer.Deserialize<AppConfig>(doc, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (deserialized is null)
                {
                    _cached = new AppConfig();
                    return _cached;
                }

                int rawVersion = 0;
                if (doc.RootElement.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.Number)
                    rawVersion = v.GetInt32();

                _cached = Migrate(deserialized, doc, rawVersion);
                if (_cached != deserialized)
                {
                    SaveToDisk(_cached);
                }
                return _cached;
            }
        }
    }

    public async Task SaveAsync(AppConfig config)
    {
        lock (_cacheLock)
        {
            _cached = config;
        }
        Directory.CreateDirectory(_configDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_configPath, json);
        ConfigChanged?.Invoke(this, config);
    }

    public bool IsFirstRun()
    {
        return !File.Exists(_configPath);
    }

    private void SaveToDisk(AppConfig config)
    {
        Directory.CreateDirectory(_configDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    private static AppConfig Migrate(AppConfig config, JsonDocument rawDoc, int rawVersion)
    {
        var migrated = config;
        if (rawVersion < 2)
        {
            migrated = migrated with { Version = 2 };
        }
        if (rawVersion < 3)
        {
            migrated = migrated with
            {
                Version = 3,
                UseCustomWhisper = false,
                CustomWhisperModelPath = ""
            };
        }
        if (rawVersion < 4)
        {
            var modes = new List<ModeConfig>(ModeDefaults.AllBuiltIns);
            var defaultModeId = ModeDefaults.StandardId;

            var root = rawDoc.RootElement;
            if (root.TryGetProperty("DefaultMode", out var defaultModeEl))
            {
                if (defaultModeEl.ValueKind == JsonValueKind.String)
                {
                    defaultModeId = defaultModeEl.GetString()?.ToLowerInvariant() ?? ModeDefaults.StandardId;
                }
                else if (defaultModeEl.ValueKind == JsonValueKind.Number && defaultModeEl.TryGetInt32(out var oldModeValue))
                {
                    defaultModeId = oldModeValue switch
                    {
                        1 => ModeDefaults.FormalId,
                        2 => ModeDefaults.RawId,
                        3 => ModeDefaults.DebugId,
                        _ => ModeDefaults.StandardId
                    };
                }
            }

            if (root.TryGetProperty("CustomSystemPrompt", out var customPromptEl) && customPromptEl.ValueKind == JsonValueKind.String)
            {
                var oldCustomPrompt = customPromptEl.GetString();
                if (!string.IsNullOrWhiteSpace(oldCustomPrompt))
                {
                    var customMode = new ModeConfig
                    {
                        Id = "custom",
                        Name = "Custom",
                        SystemPrompt = oldCustomPrompt.Trim(),
                        SkipFormatting = false,
                        ShowDiagnosticOutput = false,
                        IsBuiltIn = false
                    };
                    modes.Add(customMode);
                    defaultModeId = customMode.Id;
                }
            }

            modes = ModeDefaults.EnsureBuiltInsPresent(modes);

            migrated = migrated with
            {
                Version = 4,
                DefaultModeId = defaultModeId,
                Modes = modes
            };
        }

        if (rawVersion < 5)
        {
            migrated = migrated with
            {
                Version = 5,
                SpokenPunctuationEnabled = false
            };
        }

        if (rawVersion < 6)
        {
            migrated = migrated with
            {
                Version = 6,
                DictionaryEntries = new()
            };
        }

        if (rawVersion < 7)
        {
            migrated = migrated with
            {
                Version = 7,
                Snippets = new()
            };
        }

        if (rawVersion < 8)
        {
            var modes = ModeDefaults.EnsureBuiltInsPresent(migrated.Modes.ToList());
            migrated = migrated with
            {
                Version = 8,
                Modes = modes
            };
        }

        if (rawVersion < 9)
        {
            var defaultModeId = migrated.DefaultModeId;
            if (defaultModeId.Equals("clean", StringComparison.OrdinalIgnoreCase))
            {
                defaultModeId = ModeDefaults.StandardId;
            }
            var modes = migrated.Modes
                .Where(m => !m.Id.Equals("clean", StringComparison.OrdinalIgnoreCase))
                .ToList();
            migrated = migrated with
            {
                Version = 9,
                DefaultModeId = defaultModeId,
                Modes = modes
            };
        }

        if (rawVersion < 10)
        {
            migrated = migrated with
            {
                Version = 10,
                ListFormattingEnabled = false
            };
        }

        if (rawVersion < 11)
        {
            var modes = ModeDefaults.EnsureBuiltInsPresent(migrated.Modes.ToList());
            migrated = migrated with
            {
                Version = 11,
                Modes = modes
            };
        }

        if (rawVersion < 12)
        {
            migrated = migrated with
            {
                Version = 12,
                OverlayStyle = migrated.OverlayStyle ?? new()
            };
        }

        return migrated with
        {
            RecordingOverlay = migrated.RecordingOverlay ?? new(),
            PreviewToast = migrated.PreviewToast ?? new(),
            OverlayStyle = migrated.OverlayStyle ?? new(),
            DictionaryEntries = migrated.DictionaryEntries ?? new(),
            Snippets = migrated.Snippets ?? new()
        };
    }
}
