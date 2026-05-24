using Prompter.Models;

namespace Prompter.Services;

public interface IConfigService
{
    AppConfig Load();
    Task SaveAsync(AppConfig config);
    bool IsFirstRun();
    event EventHandler<AppConfig>? ConfigChanged;
}
