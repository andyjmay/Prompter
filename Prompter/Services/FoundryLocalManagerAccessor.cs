using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;

namespace Prompter.Services;

public class FoundryLocalManagerAccessor : IFoundryLocalManagerAccessor
{
    private readonly IFileLogger _fileLogger;
    private FoundryLocalManager? _manager;
    private readonly TaskCompletionSource _initTcs = new();
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public Task InitializationCompleted => _initTcs.Task;

    public FoundryLocalManager Manager
    {
        get
        {
            if (_manager == null)
                throw new InvalidOperationException("FoundryLocalManager not initialized. Call InitializeAsync first.");
            return _manager;
        }
    }

    public FoundryLocalManagerAccessor(IFileLogger fileLogger)
    {
        _fileLogger = fileLogger;
    }

    public async Task InitializeAsync(int idleTtlMinutes)
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            var config = new Configuration
            {
                AppName = "Prompter",
                LogLevel = Microsoft.AI.Foundry.Local.LogLevel.Information
            };

            var factory = LoggerFactory.Create(b => b.AddProvider(new FileLogProvider(_fileLogger)));
            var logger = factory.CreateLogger<FoundryLocalManagerAccessor>();

            try
            {
                await InitializeManagerAsync(config, logger);
                _initialized = true;
                _initTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _fileLogger.LogException(ex, "FoundryLocalManager initialization failed");
                _initTcs.TrySetException(ex);
                throw;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    protected virtual async Task InitializeManagerAsync(Configuration config, ILogger logger)
    {
        await FoundryLocalManager.CreateAsync(config, logger);
        _manager = FoundryLocalManager.Instance;

        if (_manager == null)
            throw new InvalidOperationException("FoundryLocalManager.Instance is null after CreateAsync.");

        _fileLogger.Log("Foundry Local manager initialized.");

        try
        {
            await _manager.DownloadAndRegisterEpsAsync((ep, pct) => { });
        }
        catch (Exception ex)
        {
            _fileLogger.LogException(ex, "Execution provider registration failed");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _initialized = false;
        // Do not dispose _initLock here; it may be held by a concurrent InitializeAsync.
        // SemaphoreSlim can be safely left undisposed during app teardown.
        if (_manager != null)
        {
            _manager.Dispose();
            _manager = null;
        }
    }

    private class FileLogProvider : ILoggerProvider
    {
        private readonly IFileLogger _logger;
        public FileLogProvider(IFileLogger logger) => _logger = logger;
        public ILogger CreateLogger(string categoryName) => new Wrapper(_logger, categoryName);
        public void Dispose() { }

        private class Wrapper : ILogger
        {
            private readonly IFileLogger _logger;
            private readonly string _cat;
            public Wrapper(IFileLogger logger, string cat) { _logger = logger; _cat = cat; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _logger.Log($"[{_cat}] {formatter(state, exception)}");
            }
        }
    }
}
