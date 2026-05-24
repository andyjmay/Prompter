using System.Runtime.InteropServices;
using System.Windows;

namespace Prompter.Services;

public static class ScreenHelper
{
    public static RECT GetActiveMonitorWorkArea()
    {
        var hwnd = GetForegroundWindow();
        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(hMonitor, ref mi))
        {
            return mi.rcWork;
        }
        return new RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)SystemParameters.WorkArea.Width,
            Bottom = (int)SystemParameters.WorkArea.Height
        };
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
