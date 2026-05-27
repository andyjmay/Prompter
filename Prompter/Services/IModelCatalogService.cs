using Prompter.Models;

namespace Prompter.Services;

public interface IModelCatalogService
{
    Task<List<(string Alias, string DisplayName)>> ListAvailableWhisperModelsAsync(CancellationToken ct = default);
    Task<List<ModelStatusInfo>> GetModelStatusListAsync(CancellationToken ct = default);
    Task<bool> IsModelInCatalogAsync(string alias, CancellationToken ct = default);
    Task<bool> IsModelCachedAsync(string alias, CancellationToken ct = default);
    Task<List<(string Alias, string DisplayName, string SizeDescription)>> ListAvailableChatModelsAsync(CancellationToken ct = default);
    Task<string?> GetModelDisplayNameAsync(string alias, CancellationToken ct = default);
}
