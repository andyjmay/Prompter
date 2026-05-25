using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeDialogService : IDialogService
{
    public List<(string Title, string Message)> Warnings { get; } = new();
    public List<(string Title, string Message)> Errors { get; } = new();
    public List<(string Title, string Message)> Infos { get; } = new();

    public void ShowError(string title, string message) => Errors.Add((title, message));

    public void ShowWarning(string title, string message) => Warnings.Add((title, message));

    public void ShowInfo(string title, string message) => Infos.Add((title, message));

    public bool ShowSettingsDialog(
        IConfigService configService,
        IClipboardService clipboardService,
        IStartupService startupService,
        IFileLogger logger,
        IModelCatalogService modelCatalog,
        IModelManager modelManager,
        ITextFormatter textFormatter,
        IHuggingFaceService hfService,
        IGgufModelStore ggufStore,
        IInputInjectorService inputInjectorService) => false;

    public bool ShowWelcomeDialog(string hotkeyDisplay) => false;
}
