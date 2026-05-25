namespace Prompter.Services;

using Prompter.Models;

public interface IThemeService
{
    OverlayTheme CurrentTheme { get; }
    void ApplyTheme(OverlayTheme theme);
}
