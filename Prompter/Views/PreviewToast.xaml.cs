using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Prompter.Services;

namespace Prompter.Views;

public partial class PreviewToast : Window
{
    private readonly ClipboardService _clipboard;
    private readonly DispatcherTimer _timer;
    private bool _mouseOver;

    public PreviewToast(string text, ClipboardService clipboard)
    {
        InitializeComponent();
        _clipboard = clipboard;
        OutputText.Text = text;

        var screen = SystemParameters.WorkArea;
        Left = screen.Width - Width - 16;
        Top = screen.Height - Height - 16;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) =>
        {
            if (!_mouseOver) Close();
        };

        MouseEnter += (_, _) => _mouseOver = true;
        MouseLeave += (_, _) => _mouseOver = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Prevent the toast from stealing focus
        var helper = new WindowInteropHelper(this);
        NativeMethods.SetWindowPos(helper.Handle, IntPtr.Zero, 0, 0, 0, 0,
            0x0010 /* SWP_NOACTIVATE */ | 0x0001 /* SWP_NOSIZE */ | 0x0002 /* SWP_NOMOVE */ | 0x0004 /* SWP_NOZORDER */);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _timer.Start();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        _clipboard.CopyText(OutputText.Text);
        Close();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
