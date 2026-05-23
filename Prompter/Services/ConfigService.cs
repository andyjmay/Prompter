using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Prompter.Models;

namespace Prompter.Services;

public class ConfigService
{
    private readonly string _configDir;
    private readonly string _configPath;
    private AppConfig? _cached;
    private readonly object _cacheLock = new();

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
                var json = JsonSerializer.Serialize(_cached, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
                return _cached;
            }

            var jsonText = File.ReadAllText(_configPath);
            _cached = JsonSerializer.Deserialize<AppConfig>(jsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? new AppConfig();
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
    }

    public bool IsFirstRun()
    {
        return !File.Exists(_configPath);
    }
}
