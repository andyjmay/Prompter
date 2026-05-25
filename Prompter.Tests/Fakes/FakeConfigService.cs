using Prompter.Models;
using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeConfigService : IConfigService
{
    private AppConfig _baseConfig;
    private readonly Stack<AppConfig> _overrideStack = new();

    public FakeConfigService(AppConfig config)
    {
        _baseConfig = config;
    }

    public AppConfig Load() => _overrideStack.Count > 0 ? _overrideStack.Peek() : _baseConfig;

    public Task SaveAsync(AppConfig config)
    {
        _baseConfig = config;
        ConfigChanged?.Invoke(this, config);
        return Task.CompletedTask;
    }

    public bool IsFirstRunResult { get; set; } = false;
    public bool IsFirstRun() => IsFirstRunResult;

    public event EventHandler<AppConfig>? ConfigChanged;

    public IDisposable PushTemporaryConfig(AppConfig config)
    {
        var scope = new TempConfigScope(this);
        _overrideStack.Push(config);
        return scope;
    }

    private void PopTemporaryConfig()
    {
        if (_overrideStack.Count > 0)
            _overrideStack.Pop();
    }

    private sealed class TempConfigScope : IDisposable
    {
        private readonly FakeConfigService _service;
        private bool _disposed;

        public TempConfigScope(FakeConfigService service)
        {
            _service = service;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _service.PopTemporaryConfig();
            }
        }
    }
}
