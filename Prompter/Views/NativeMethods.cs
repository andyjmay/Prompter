using System.Runtime.InteropServices;

namespace Prompter.Views;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
