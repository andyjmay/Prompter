using System.Windows;

namespace Prompter.Services;

public class ExceptionHandler : IExceptionHandler
{
    private readonly IFileLogger _logger;
    private readonly IDialogService _dialogService;

    public ExceptionHandler(IFileLogger logger, IDialogService dialogService)
    {
        _logger = logger;
        _dialogService = dialogService;
    }

    public void HandleDispatcherException(Exception ex)
    {
        _logger.LogException(ex, "DispatcherUnhandledException");
        _dialogService.ShowError(
            "Prompter Error",
            $"An unexpected error occurred:\n{ex.Message}\n\nPrompter will continue running, but you may want to restart it.");
    }

    public void HandleUnobservedTaskException(AggregateException ex)
    {
        _logger.LogException(ex, "UnobservedTaskException");
    }

    public void HandleFatalException(Exception ex)
    {
        _logger.LogException(ex, "UnhandledException");
    }
}
