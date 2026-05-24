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

    public event EventHandler<AppConfig>? ConfigChanged;

    public ConfigService()
    {
        _configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Prompter");
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

            var jsonText = File.ReadAllText(_configPath);
            var deserialized = JsonSerializer.Deserialize<AppConfig>(jsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (deserialized is null)
            {
                _cached = new AppConfig();
                return _cached;
            }

            _cached = Migrate(deserialized);
            if (_cached != deserialized)
            {
                SaveToDisk(_cached);
            }
            return _cached;
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

    private static AppConfig Migrate(AppConfig config)
    {
        var migrated = config.Version switch
        {
            < 2 => config with { Version = 2 },
            _ => config
        };

        return migrated with
        {
            RecordingOverlay = migrated.RecordingOverlay ?? new(),
            PreviewToast = migrated.PreviewToast ?? new(),
            OverlayStyle = migrated.OverlayStyle ?? new()
        };
    }
}
