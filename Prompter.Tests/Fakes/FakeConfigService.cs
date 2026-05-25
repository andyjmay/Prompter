using Prompter.Models;
using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeConfigService : IConfigService
{
    private AppConfig _config;

    public FakeConfigService(AppConfig config)
    {
        _config = config;
    }

    public AppConfig Load() => _config;

    public Task SaveAsync(AppConfig config)
    {
        _config = config;
        ConfigChanged?.Invoke(this, config);
        return Task.CompletedTask;
    }

    public bool IsFirstRun() => false;

    public event EventHandler<AppConfig>? ConfigChanged;
}
