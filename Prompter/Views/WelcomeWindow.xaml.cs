using System.Windows;
using Prompter.Models;

namespace Prompter.Views;

public partial class WelcomeWindow : Window
{
    private readonly AppConfig _config;

    public WelcomeWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        HotkeyText.Text = string.IsNullOrEmpty(_config.HotkeyKey)
            ? _config.HotkeyModifiers
            : $"{_config.HotkeyModifiers} + {_config.HotkeyKey}";
    }

    private void GotIt_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
