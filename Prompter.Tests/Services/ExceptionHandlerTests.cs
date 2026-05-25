using Prompter.Services;
using Prompter.Tests.Fakes;
using Xunit;

namespace Prompter.Tests.Services;

public class ExceptionHandlerTests
{
    private readonly FakeFileLogger _logger;
    private readonly FakeDialogService _dialog;
    private readonly ExceptionHandler _handler;

    public ExceptionHandlerTests()
    {
        _logger = new FakeFileLogger();
        _dialog = new FakeDialogService();
        _handler = new ExceptionHandler(_logger, _dialog);
    }

    [Fact]
    public void HandleDispatcherException_LogsAndShowsError()
    {
        var ex = new InvalidOperationException("Dispatcher fault");

        _handler.HandleDispatcherException(ex);

        Assert.Single(_dialog.Errors);
        Assert.Equal("Prompter Error", _dialog.Errors[0].Title);
        Assert.Contains("Dispatcher fault", _dialog.Errors[0].Message);
        Assert.Contains("DispatcherUnhandledException", _logger.LogBuilder.ToString());
    }

    [Fact]
    public void HandleUnobservedTaskException_LogsOnly()
    {
        var ex = new AggregateException(new InvalidOperationException("Task fault"));

        _handler.HandleUnobservedTaskException(ex);

        Assert.Empty(_dialog.Errors);
        Assert.Empty(_dialog.Warnings);
        Assert.Contains("UnobservedTaskException", _logger.LogBuilder.ToString());
    }

    [Fact]
    public void HandleFatalException_LogsOnly()
    {
        var ex = new Exception("Fatal fault");

        _handler.HandleFatalException(ex);

        Assert.Empty(_dialog.Errors);
        Assert.Empty(_dialog.Warnings);
        Assert.Contains("UnhandledException", _logger.LogBuilder.ToString());
    }
}
