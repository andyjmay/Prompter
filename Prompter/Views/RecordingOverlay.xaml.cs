using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Prompter.Services;

namespace Prompter.Views;

public partial class RecordingOverlay : Window
{
    public RecordingOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var screen = ScreenHelper.GetActiveMonitorWorkArea();
        Left = screen.Left + (screen.Width - Width) / 2;
        Top = screen.Top + 40;

        if (Resources["PulseStoryboard"] is Storyboard sb)
        {
            sb.Begin();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var helper = new WindowInteropHelper(this);
        NativeMethods.SetWindowPos(helper.Handle, IntPtr.Zero, 0, 0, 0, 0,
            0x0010 | 0x0001 | 0x0002 | 0x0004);
    }
}
