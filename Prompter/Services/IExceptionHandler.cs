namespace Prompter.Services;

public interface IExceptionHandler
{
    void HandleDispatcherException(Exception ex);
    void HandleUnobservedTaskException(AggregateException ex);
    void HandleFatalException(Exception ex);
}
