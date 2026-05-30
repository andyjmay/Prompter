using System.Windows;
using Prompter.Views;

namespace Prompter.Services;

public class DialogService : IDialogService
{
    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowWarning(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public bool ShowSettingsDialog(IConfigService configService, IClipboardService clipboardService, IStartupService startupService, IFileLogger logger, IModelCatalogService modelCatalog, IModelManager modelManager, ITextFormatter textFormatter, IHuggingFaceService hfService, IGgufModelStore ggufStore, IInputInjectorService inputInjectorService, ITranscriptionService transcriptionService, IAudioRecorderService audioRecorderService, IThemeService themeService, IHotkeyService hotkeyService)
    {
        var settings = new SettingsWindow(configService, clipboardService, startupService, logger, modelCatalog, modelManager, textFormatter, hfService, ggufStore, inputInjectorService, transcriptionService, audioRecorderService, this, themeService, hotkeyService);
        return settings.ShowDialog() == true;
    }

    public bool ShowWelcomeDialog(string hotkeyDisplay)
    {
        var welcome = new WelcomeWindow(hotkeyDisplay);
        return welcome.ShowDialog() == true;
    }

    public void ShowModelTestingDialog(Window owner, IConfigService configService, IModelCatalogService modelCatalog, IModelManager modelManager, ITranscriptionService transcriptionService, IAudioRecorderService audioRecorderService, ITextFormatter textFormatter, IFileLogger logger, IGgufModelStore ggufStore)
    {
        var testingWindow = new ModelTestingWindow(
            configService,
            modelCatalog,
            modelManager,
            transcriptionService,
            audioRecorderService,
            textFormatter,
            logger,
            ggufStore
        )
        {
            Owner = owner
        };
        testingWindow.ShowDialog();
    }
}

