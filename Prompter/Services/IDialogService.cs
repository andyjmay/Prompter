namespace Prompter.Services;

public interface IDialogService
{
    void ShowError(string title, string message);
    void ShowWarning(string title, string message);
    void ShowInfo(string title, string message);
    bool ShowSettingsDialog(IConfigService configService, IClipboardService clipboardService, IStartupService startupService, IFileLogger logger, IModelCatalogService modelCatalog, IModelManager modelManager, ITextFormatter textFormatter, IHuggingFaceService hfService, IGgufModelStore ggufStore, IInputInjectorService inputInjectorService, ITranscriptionService transcriptionService, IAudioRecorderService audioRecorderService, IThemeService themeService);
    bool ShowWelcomeDialog(string hotkeyDisplay);
    void ShowModelTestingDialog(System.Windows.Window owner, IConfigService configService, IModelCatalogService modelCatalog, IModelManager modelManager, ITranscriptionService transcriptionService, IAudioRecorderService audioRecorderService, ITextFormatter textFormatter, IFileLogger logger, IGgufModelStore ggufStore);
}

