using System.Windows.Threading;

namespace Prompter.Tests.Helpers;

public class DispatcherFixture : IDisposable
{
    private readonly Thread _staThread;
    private readonly Dispatcher _dispatcher;

    public DispatcherFixture()
    {
        var tcs = new TaskCompletionSource<Dispatcher>();
        _staThread = new Thread(() =>
        {
            tcs.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        });
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        _dispatcher = tcs.Task.Result;
    }

    public Dispatcher Dispatcher => _dispatcher;

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _staThread.Join(TimeSpan.FromSeconds(5));
    }
}
