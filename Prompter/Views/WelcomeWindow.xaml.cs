using System.Windows;

namespace Prompter.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow(string hotkeyDisplay)
    {
        InitializeComponent();
        HotkeyText.Text = hotkeyDisplay;
    }

    private void GotIt_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
