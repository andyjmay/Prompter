using System.Windows.Media;
using Prompter.Models;
using Prompter.Services;
using Xunit;

namespace Prompter.Tests.Services;

public class ThemeResolverTests
{
    [Fact]
    public void Resolve_DarkTheme_ReturnsDarkColors()
    {
        var config = new OverlayStyleConfig
        {
            Theme = OverlayTheme.Dark,
            BackgroundOpacity = 1.0,
            ToastOpacity = 1.0
        };

        var brushes = ThemeResolver.Resolve(config);

        Assert.NotNull(brushes);
        var bgBrush = Assert.IsType<SolidColorBrush>(brushes.OverlayBackground);
        Assert.Equal(Color.FromArgb(255, 0, 0, 0), bgBrush.Color);

        var accentBrush = Assert.IsType<SolidColorBrush>(brushes.Accent);
        Assert.Equal(Color.FromArgb(255, 0xFF, 0x33, 0x33), accentBrush.Color);

        var textBrush = Assert.IsType<SolidColorBrush>(brushes.PrimaryText);
        Assert.Equal(Colors.White, textBrush.Color);
    }

    [Fact]
    public void Resolve_LightTheme_ReturnsLightColors()
    {
        var config = new OverlayStyleConfig
        {
            Theme = OverlayTheme.Light,
            BackgroundOpacity = 1.0,
            ToastOpacity = 1.0
        };

        var brushes = ThemeResolver.Resolve(config);

        Assert.NotNull(brushes);
        var bgBrush = Assert.IsType<SolidColorBrush>(brushes.OverlayBackground);
        Assert.Equal(Colors.White, bgBrush.Color);

        var accentBrush = Assert.IsType<SolidColorBrush>(brushes.Accent);
        Assert.Equal(Color.FromArgb(255, 0, 0x66, 0xCC), accentBrush.Color);

        var textBrush = Assert.IsType<SolidColorBrush>(brushes.PrimaryText);
        Assert.Equal(Colors.Black, textBrush.Color);
    }

    [Fact]
    public void Resolve_HighContrastTheme_ReturnsHighContrastColors()
    {
        var config = new OverlayStyleConfig
        {
            Theme = OverlayTheme.HighContrast,
            BackgroundOpacity = 1.0,
            ToastOpacity = 1.0
        };

        var brushes = ThemeResolver.Resolve(config);

        Assert.NotNull(brushes);
        var bgBrush = Assert.IsType<SolidColorBrush>(brushes.OverlayBackground);
        Assert.Equal(Colors.Black, bgBrush.Color);

        var accentBrush = Assert.IsType<SolidColorBrush>(brushes.Accent);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00), accentBrush.Color); // Yellow

        var textBrush = Assert.IsType<SolidColorBrush>(brushes.PrimaryText);
        Assert.Equal(Colors.White, textBrush.Color);
    }

    [Fact]
    public void Resolve_MinimalTheme_ReturnsMinimalColors()
    {
        var config = new OverlayStyleConfig
        {
            Theme = OverlayTheme.Minimal,
            BackgroundOpacity = 1.0,
            ToastOpacity = 1.0
        };

        var brushes = ThemeResolver.Resolve(config);

        Assert.NotNull(brushes);
        var bgBrush = Assert.IsType<SolidColorBrush>(brushes.OverlayBackground);
        Assert.Equal(Color.FromArgb(0, 0, 0, 0), bgBrush.Color);

        var accentBrush = Assert.IsType<SolidColorBrush>(brushes.Accent);
        Assert.Equal(Colors.White, accentBrush.Color);
    }

    [Fact]
    public void Resolve_AppliesCustomColors()
    {
        var config = new OverlayStyleConfig
        {
            Theme = OverlayTheme.Dark,
            AccentColor = "#00FF00", // Green
            TextColor = "#FFA500", // Orange
            ProcessingAccentColor = "#0000FF", // Blue
            OverlayBackgroundColor = "#FF0000", // Red
            ToastBackgroundColor = "#808080", // Gray
            BackgroundOpacity = 1.0,
            ToastOpacity = 1.0
        };

        var brushes = ThemeResolver.Resolve(config);

        Assert.Equal(Color.FromRgb(0, 255, 0), ((SolidColorBrush)brushes.Accent).Color);
        Assert.Equal(Color.FromRgb(255, 165, 0), ((SolidColorBrush)brushes.PrimaryText).Color);
        Assert.Equal(Color.FromRgb(255, 165, 0), ((SolidColorBrush)brushes.SecondaryText).Color);
        Assert.Equal(Color.FromRgb(0, 0, 255), ((SolidColorBrush)brushes.ProcessingAccent).Color);
        Assert.Equal(Color.FromRgb(255, 0, 0), ((SolidColorBrush)brushes.OverlayBackground).Color);
        Assert.Equal(Color.FromRgb(128, 128, 128), ((SolidColorBrush)brushes.ToastBackground).Color);
    }

    [Fact]
    public void Resolve_AppliesOpacityCorrectly()
    {
        var config = new OverlayStyleConfig
        {
            Theme = OverlayTheme.Dark,
            BackgroundOpacity = 0.5,
            ToastOpacity = 0.25
        };

        var brushes = ThemeResolver.Resolve(config);

        // Dark default overlay background color is #000000. Alpha is 255.
        // 255 * 0.5 = 127.5 -> rounded to 128 (or 127 depending on Math.Round)
        var bgBrush = Assert.IsType<SolidColorBrush>(brushes.OverlayBackground);
        Assert.Equal(0, bgBrush.Color.R);
        Assert.Equal(0, bgBrush.Color.G);
        Assert.Equal(0, bgBrush.Color.B);
        Assert.True(bgBrush.Color.A > 120 && bgBrush.Color.A < 135);

        // Dark default toast background is #FF2D2D2D. Alpha is 255.
        // 255 * 0.25 = 63.75 -> rounded to 64
        var toastBrush = Assert.IsType<SolidColorBrush>(brushes.ToastBackground);
        Assert.True(toastBrush.Color.A > 60 && toastBrush.Color.A < 68);
    }

    [Fact]
    public void Resolve_OpacityClampsCorrectly()
    {
        var config = new OverlayStyleConfig
        {
            Theme = OverlayTheme.Dark,
            BackgroundOpacity = 5.0, // Should clamp to 1.0
            ToastOpacity = -2.0 // Should clamp to 0.0
        };

        var brushes = ThemeResolver.Resolve(config);

        var bgBrush = Assert.IsType<SolidColorBrush>(brushes.OverlayBackground);
        Assert.Equal(255, bgBrush.Color.A);

        var toastBrush = Assert.IsType<SolidColorBrush>(brushes.ToastBackground);
        Assert.Equal(0, toastBrush.Color.A);
    }

    [Fact]
    public void Resolve_IgnoresInvalidCustomColorsAndUsesThemeDefault()
    {
        var config = new OverlayStyleConfig
        {
            Theme = OverlayTheme.Dark,
            AccentColor = "invalid_color_format",
            BackgroundOpacity = 1.0
        };

        var brushes = ThemeResolver.Resolve(config);

        // Should fallback to theme default for Dark, which is #FF3333
        var accentBrush = Assert.IsType<SolidColorBrush>(brushes.Accent);
        Assert.Equal(Color.FromArgb(255, 0xFF, 0x33, 0x33), accentBrush.Color);
    }

    [Fact]
    public void Resolve_ReturnsFrozenBrushes()
    {
        var config = new OverlayStyleConfig();
        var brushes = ThemeResolver.Resolve(config);

        Assert.True(brushes.OverlayBackground.IsFrozen);
        Assert.True(brushes.OverlayBorder.IsFrozen);
        Assert.True(brushes.Accent.IsFrozen);
        Assert.True(brushes.ProcessingAccent.IsFrozen);
        Assert.True(brushes.PrimaryText.IsFrozen);
        Assert.True(brushes.SecondaryText.IsFrozen);
        Assert.True(brushes.ToastBackground.IsFrozen);
        Assert.True(brushes.ToastBorder.IsFrozen);
        Assert.True(brushes.ButtonBackground.IsFrozen);
        Assert.True(brushes.ButtonBorder.IsFrozen);
    }
}
