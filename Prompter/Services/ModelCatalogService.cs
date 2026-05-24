using Prompter.Models;

namespace Prompter.Services;

public class ModelCatalogService : IModelCatalogService
{
    private readonly IFoundryLocalManagerAccessor _accessor;
    private readonly IFileLogger _logger;

    public ModelCatalogService(IFoundryLocalManagerAccessor accessor, IFileLogger logger)
    {
        _accessor = accessor;
        _logger = logger;
    }

    public async Task<List<(string Alias, string DisplayName)>> ListAvailableWhisperModelsAsync(CancellationToken ct = default)
    {
        await _accessor.InitializationCompleted;
        var catalog = await _accessor.Manager.GetCatalogAsync();
        var models = await catalog.ListModelsAsync(ct);
        var result = new List<(string Alias, string DisplayName)>();

        foreach (var model in models)
        {
            var alias = model.Alias;
            if (alias.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase))
            {
                var displayName = !string.IsNullOrWhiteSpace(model.Info?.DisplayName)
                    ? model.Info.DisplayName
                    : alias;
                result.Add((alias, displayName));
            }
        }

        return result.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<ModelStatusInfo>> GetModelStatusListAsync(CancellationToken ct = default)
    {
        try
        {
            await _accessor.InitializationCompleted;
            var catalog = await _accessor.Manager.GetCatalogAsync();
            var models = await catalog.ListModelsAsync(ct);
            var result = new List<ModelStatusInfo>();

            foreach (var m in models)
            {
                var alias = m.Alias;
                var info = m.Info;
                string task = info?.Task ?? "Unknown";
                bool isWhisper = alias.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase);
                bool isChat = !isWhisper &&
                              (string.IsNullOrEmpty(task) ||
                               (!task.Contains("embed", StringComparison.OrdinalIgnoreCase) &&
                                !task.Contains("speech", StringComparison.OrdinalIgnoreCase) &&
                                !task.Contains("audio", StringComparison.OrdinalIgnoreCase)));

                if (!isWhisper && !isChat) continue;

                bool isCached = false;
                try { isCached = await m.IsCachedAsync(); } catch { }

                string size = ModelCatalog.GetSizeDescription(alias);

                result.Add(new ModelStatusInfo
                {
                    Alias = alias,
                    DisplayName = info?.DisplayName ?? alias,
                    IsCached = isCached,
                    IsLoaded = false, // ModelManager owns loaded state, updated by caller
                    SizeDescription = size,
                    TaskType = isWhisper ? "Speech Transcription" : "Text Correction"
                });
            }

            return result.OrderBy(r => r.TaskType).ThenBy(r => r.DisplayName).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, "GetModelStatusListAsync");
            return new List<ModelStatusInfo>();
        }
    }

    public async Task<bool> IsModelInCatalogAsync(string alias)
    {
        try
        {
            await _accessor.InitializationCompleted;
            var catalog = await _accessor.Manager.GetCatalogAsync();
            var model = await catalog.GetModelAsync(alias);
            return model != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<(string Alias, string DisplayName)>> ListAvailableChatModelsAsync(CancellationToken ct = default)
    {
        await _accessor.InitializationCompleted;
        var catalog = await _accessor.Manager.GetCatalogAsync();
        var models = await catalog.ListModelsAsync(ct);
        var result = new List<(string Alias, string DisplayName)>();

        foreach (var model in models)
        {
            var alias = model.Alias;
            var info = model.Info;

            if (alias.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase))
                continue;

            var task = info?.Task;
            if (!string.IsNullOrEmpty(task))
            {
                if (task.Contains("embed", StringComparison.OrdinalIgnoreCase) ||
                    task.Contains("speech", StringComparison.OrdinalIgnoreCase) ||
                    task.Contains("audio", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var displayName = !string.IsNullOrWhiteSpace(info?.DisplayName)
                ? info.DisplayName
                : alias;

            result.Add((alias, displayName));
        }

        return result.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string?> GetModelDisplayNameAsync(string alias, CancellationToken ct = default)
    {
        try
        {
            await _accessor.InitializationCompleted;
            var catalog = await _accessor.Manager.GetCatalogAsync();
            var model = await catalog.GetModelAsync(alias, ct);
            return model?.Info?.DisplayName;
        }
        catch
        {
            return null;
        }
    }
}
