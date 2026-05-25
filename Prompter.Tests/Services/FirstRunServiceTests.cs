using Prompter.Models;
using Prompter.Services;
using Prompter.Tests.Fakes;
using Xunit;

namespace Prompter.Tests.Services;

public class FirstRunServiceTests
{
    [Fact]
    public async Task CheckAndShowAsync_FirstRun_ShowsWelcome()
    {
        var configService = new FakeConfigService(new AppConfig());
        configService.IsFirstRunResult = true;
        var dialog = new FakeDialogService();
        var service = new FirstRunService(configService, dialog);

        await service.CheckAndShowAsync();

        Assert.Single(dialog.WelcomeDialogs);
    }

    [Fact]
    public async Task CheckAndShowAsync_NotFirstRun_DoesNotShowWelcome()
    {
        var configService = new FakeConfigService(new AppConfig());
        configService.IsFirstRunResult = false;
        var dialog = new FakeDialogService();
        var service = new FirstRunService(configService, dialog);

        await service.CheckAndShowAsync();

        Assert.Empty(dialog.Infos);
        Assert.Empty(dialog.Errors);
        Assert.Empty(dialog.Warnings);
    }

    [Fact]
    public async Task CheckAndShowAsync_FirstRun_HotkeyDisplayFormatted()
    {
        var config = new AppConfig
        {
            HotkeyModifiers = "Ctrl + Shift",
            HotkeyKey = "F9"
        };
        var configService = new FakeConfigService(config);
        configService.IsFirstRunResult = true;
        var dialog = new FakeDialogService();
        var service = new FirstRunService(configService, dialog);

        await service.CheckAndShowAsync();

        Assert.Single(dialog.WelcomeDialogs);
        Assert.Equal("Ctrl + Shift + F9", dialog.WelcomeDialogs[0]);
    }
}
