using System.Windows;
using Prompter.Models;

namespace Prompter.Services;

public class ThemeService : IThemeService
{
    public OverlayTheme CurrentTheme { get; private set; } = OverlayTheme.Dark;

    public void ApplyTheme(OverlayTheme theme)
    {
        if (CurrentTheme == theme) return;
        CurrentTheme = theme;

        var app = Application.Current;
        if (app == null) return;

        var dictionaries = app.Resources.MergedDictionaries;

        string themePath = theme switch
        {
            OverlayTheme.Light => "Themes/LightTheme.xaml",
            _ => "Themes/DarkTheme.xaml"
        };

        // Replace the first merged dictionary (the theme) so all other app-level
        // resources (e.g. from third-party controls) are preserved.
        if (dictionaries.Count > 0)
        {
            dictionaries[0] = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };
        }
        else
        {
            dictionaries.Add(new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) });
        }
    }
}
