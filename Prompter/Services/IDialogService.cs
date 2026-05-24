namespace Prompter.Services;

public interface IDialogService
{
    void ShowError(string title, string message);
    void ShowWarning(string title, string message);
    void ShowInfo(string title, string message);
    bool ShowSettingsDialog(IConfigService configService, IClipboardService clipboardService, IStartupService startupService, IFileLogger logger, IModelCatalogService modelCatalog, IModelManager modelManager, ITextFormatter textFormatter);
    bool ShowWelcomeDialog(string hotkeyDisplay);
}
