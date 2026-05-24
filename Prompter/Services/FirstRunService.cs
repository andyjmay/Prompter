namespace Prompter.Services;

public class FirstRunService : IFirstRunService
{
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;

    public FirstRunService(IConfigService configService, IDialogService dialogService)
    {
        _configService = configService;
        _dialogService = dialogService;
    }

    public Task CheckAndShowAsync()
    {
        if (_configService.IsFirstRun())
        {
            var config = _configService.Load();
            var hotkeyDisplay = string.IsNullOrEmpty(config.HotkeyKey)
                ? config.HotkeyModifiers
                : $"{config.HotkeyModifiers} + {config.HotkeyKey}";
            _dialogService.ShowWelcomeDialog(hotkeyDisplay);
        }
        return Task.CompletedTask;
    }
}
