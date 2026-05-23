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
}
