using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views;

public partial class PreviewToast : Window, IDisposable
{
    private readonly IClipboardService _clipboard;
    private readonly PreviewToastSpecificConfig _config;
    private readonly DispatcherTimer _timer;
    private bool _mouseOver;
    private bool _disposed;

    public PreviewToast(string text, IClipboardService clipboard, PreviewToastSpecificConfig config, OverlayStyleConfig style)
    {
        InitializeComponent();
        _clipboard = clipboard;
        _config = config;
        OutputText.Text = text;

        var brushes = ThemeResolver.Resolve(style);
        RootBorder.Background = brushes.ToastBackground;
        RootBorder.BorderBrush = brushes.ToastBorder;
        TitleText.Foreground = brushes.PrimaryText;
        OutputText.Foreground = brushes.SecondaryText;
        CopyButton.Background = brushes.ButtonBackground;
        CopyButton.Foreground = brushes.PrimaryText;
        CopyButton.BorderBrush = brushes.ButtonBorder;
        DismissButton.Background = brushes.ButtonBackground;
        DismissButton.Foreground = brushes.PrimaryText;
        DismissButton.BorderBrush = brushes.ButtonBorder;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(config.DurationSeconds) };
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
        var helper = new WindowInteropHelper(this);
        NativeMethods.SetWindowPos(helper.Handle, IntPtr.Zero, 0, 0, 0, 0,
            0x0010 | 0x0001 | 0x0002 | 0x0004);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var (left, top) = OverlayPositioner.ComputePosition(
            this,
            _config.Placement.Anchor,
            _config.Placement.OffsetX,
            _config.Placement.OffsetY);
        Left = left;
        Top = top;

        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_disposed)
        {
            _disposed = true;
            _timer.Stop();
        }
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _timer.Stop();
            Dispatcher.Invoke(() => Close());
        }
    }
}
