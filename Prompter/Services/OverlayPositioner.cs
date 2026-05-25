using System.Windows;
using Prompter.Models;
using static Prompter.Services.ScreenHelper;

namespace Prompter.Services;

public static class OverlayPositioner
{
    public static (double Left, double Top) ComputePosition(
        Window window,
        OverlayAnchor anchor,
        int offsetX,
        int offsetY)
    {
        var screen = GetActiveMonitorWorkArea(); // device pixels

        double dipLeft = screen.Left;
        double dipTop = screen.Top;
        double dipRight = screen.Right;
        double dipBottom = screen.Bottom;

        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget != null)
        {
            var fromDevice = source.CompositionTarget.TransformFromDevice;
            var tl = fromDevice.Transform(new Point(screen.Left, screen.Top));
            var br = fromDevice.Transform(new Point(screen.Right, screen.Bottom));
            dipLeft = tl.X;
            dipTop = tl.Y;
            dipRight = br.X;
            dipBottom = br.Y;
        }

        double windowWidth = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
        double windowHeight = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;

        double left = anchor switch
        {
            OverlayAnchor.TopLeft or OverlayAnchor.MiddleLeft or OverlayAnchor.BottomLeft => dipLeft + offsetX,
            OverlayAnchor.TopCenter or OverlayAnchor.Center or OverlayAnchor.BottomCenter => dipLeft + (dipRight - dipLeft - windowWidth) / 2.0 + offsetX,
            OverlayAnchor.TopRight or OverlayAnchor.MiddleRight or OverlayAnchor.BottomRight => dipRight - windowWidth + offsetX,
            _ => dipLeft + (dipRight - dipLeft - windowWidth) / 2.0 + offsetX
        };

        double top = anchor switch
        {
            OverlayAnchor.TopLeft or OverlayAnchor.TopCenter or OverlayAnchor.TopRight => dipTop + offsetY,
            OverlayAnchor.MiddleLeft or OverlayAnchor.Center or OverlayAnchor.MiddleRight => dipTop + (dipBottom - dipTop - windowHeight) / 2.0 + offsetY,
            OverlayAnchor.BottomLeft or OverlayAnchor.BottomCenter or OverlayAnchor.BottomRight => dipBottom - windowHeight + offsetY,
            _ => dipTop + offsetY
        };

        left = Math.Max(dipLeft, Math.Min(left, dipRight - windowWidth));
        top = Math.Max(dipTop, Math.Min(top, dipBottom - windowHeight));

        return (left, top);
    }
}
