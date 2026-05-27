using System.Windows;

namespace Prompter.Views;

public partial class TrayIconView : Window
{
    public TrayIconView()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private async void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TrayIcon.ContextMenu?.IsOpen = false;
        TrayIcon.Visibility = Visibility.Collapsed;

        // Allow the native Win32 context menu to dismiss before shutting down,
        // otherwise it can linger on screen for several seconds.
        await Task.Delay(50);
        Application.Current.Shutdown();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        TrayIcon.Dispose();
    }
}
