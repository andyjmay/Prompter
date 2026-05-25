using System.Globalization;
using System.Windows.Media;
using Prompter.Models;

namespace Prompter.Services;

public static class ThemeResolver
{
    public record ThemeBrushes
    {
        public required Brush OverlayBackground { get; init; }
        public required Brush OverlayBorder { get; init; }
        public required Brush Accent { get; init; }
        public required Brush ProcessingAccent { get; init; }
        public required Brush PrimaryText { get; init; }
        public required Brush SecondaryText { get; init; }
        public required Brush ToastBackground { get; init; }
        public required Brush ToastBorder { get; init; }
        public required Brush ButtonBackground { get; init; }
        public required Brush ButtonBorder { get; init; }
    }

    public static ThemeBrushes Resolve(OverlayStyleConfig style)
    {
        var theme = style.Theme;
        double overlayOpacity = Math.Clamp(style.BackgroundOpacity, 0.0, 1.0);
        double toastOpacity = Math.Clamp(style.ToastOpacity, 0.0, 1.0);

        Color overlayBg, overlayBorder, accent, processingAccent, primaryText, secondaryText, toastBg, toastBorder, buttonBg, buttonBorder;

        switch (theme)
        {
            case OverlayTheme.Light:
                overlayBg = FromHex("#FFFFFF");
                overlayBorder = FromHex("#33000000");
                accent = FromHex("#0066CC");
                processingAccent = FromHex("#3399FF");
                primaryText = FromHex("#000000");
                secondaryText = FromHex("#333333");
                toastBg = FromHex("#FFF0F0F0");
                toastBorder = FromHex("#FFCCCCCC");
                buttonBg = FromHex("#FFE0E0E0");
                buttonBorder = FromHex("#FFBBBBBB");
                break;

            case OverlayTheme.HighContrast:
                overlayBg = FromHex("#000000");
                overlayBorder = FromHex("#FFFFFFFF");
                accent = FromHex("#FFFFFF00");
                processingAccent = FromHex("#00FFFF");
                primaryText = FromHex("#FFFFFFFF");
                secondaryText = FromHex("#FFFFFFFF");
                toastBg = FromHex("#FF000000");
                toastBorder = FromHex("#FFFFFFFF");
                buttonBg = FromHex("#FF000000");
                buttonBorder = FromHex("#FFFFFFFF");
                break;

            case OverlayTheme.Minimal:
                overlayBg = FromHex("#00000000");
                overlayBorder = FromHex("#00000000");
                accent = FromHex("#FFFFFFFF");
                processingAccent = FromHex("#66CCFF");
                primaryText = FromHex("#FFFFFFFF");
                secondaryText = FromHex("#FFE0E0E0");
                toastBg = FromHex("#FF1A1A1A");
                toastBorder = FromHex("#00000000");
                buttonBg = FromHex("#FF2A2A2A");
                buttonBorder = FromHex("#00000000");
                break;

            default: // Dark
                overlayBg = FromHex("#000000");
                overlayBorder = FromHex("#33FFFFFF");
                accent = FromHex("#FF3333");
                processingAccent = FromHex("#3399FF");
                primaryText = FromHex("#FFFFFFFF");
                secondaryText = FromHex("#FFE0E0E0");
                toastBg = FromHex("#FF2D2D2D");
                toastBorder = FromHex("#FF444444");
                buttonBg = FromHex("#FF3C3C3C");
                buttonBorder = FromHex("#FF555555");
                break;
        }

        if (!string.IsNullOrWhiteSpace(style.AccentColor) && TryParseColor(style.AccentColor, out var customAccent))
        {
            accent = customAccent;
        }

        if (!string.IsNullOrWhiteSpace(style.TextColor) && TryParseColor(style.TextColor, out var customText))
        {
            primaryText = customText;
            secondaryText = customText;
        }

        if (!string.IsNullOrWhiteSpace(style.ProcessingAccentColor) && TryParseColor(style.ProcessingAccentColor, out var customProcessing))
        {
            processingAccent = customProcessing;
        }

        if (!string.IsNullOrWhiteSpace(style.OverlayBackgroundColor) && TryParseColor(style.OverlayBackgroundColor, out var customOverlayBg))
        {
            overlayBg = customOverlayBg;
        }

        if (!string.IsNullOrWhiteSpace(style.ToastBackgroundColor) && TryParseColor(style.ToastBackgroundColor, out var customToastBg))
        {
            toastBg = customToastBg;
        }

        overlayBg = WithOpacity(overlayBg, overlayOpacity);
        toastBg = WithOpacity(toastBg, toastOpacity);

        return new ThemeBrushes
        {
            OverlayBackground = ToFrozenBrush(overlayBg),
            OverlayBorder = ToFrozenBrush(overlayBorder),
            Accent = ToFrozenBrush(accent),
            ProcessingAccent = ToFrozenBrush(processingAccent),
            PrimaryText = ToFrozenBrush(primaryText),
            SecondaryText = ToFrozenBrush(secondaryText),
            ToastBackground = ToFrozenBrush(toastBg),
            ToastBorder = ToFrozenBrush(toastBorder),
            ButtonBackground = ToFrozenBrush(buttonBg),
            ButtonBorder = ToFrozenBrush(buttonBorder)
        };
    }

    private static Color FromHex(string hex)
    {
        if (hex.Length == 9 && hex[0] == '#')
        {
            byte a = byte.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte r = byte.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(hex[7..9], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromArgb(a, r, g, b);
        }

        if (hex.Length == 7 && hex[0] == '#')
        {
            byte r = byte.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromRgb(r, g, b);
        }

        throw new FormatException($"Invalid hex color format: {hex}");
    }

    private static bool TryParseColor(string value, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            color = Colors.Transparent;
            return false;
        }
    }

    private static Color WithOpacity(Color color, double opacity)
    {
        byte a = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    private static Brush ToFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }
}
