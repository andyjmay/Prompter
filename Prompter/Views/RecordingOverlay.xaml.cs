using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

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
        // Position at top-center of primary screen
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = screen.Top + 40;

        // Start pulsing animation
        if (Resources["PulseStoryboard"] is Storyboard sb)
        {
            sb.Begin();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Prevent the overlay from stealing focus from the target window
        var helper = new WindowInteropHelper(this);
        NativeMethods.SetWindowPos(helper.Handle, IntPtr.Zero, 0, 0, 0, 0,
            0x0010 /* SWP_NOACTIVATE */ | 0x0001 /* SWP_NOSIZE */ | 0x0002 /* SWP_NOMOVE */ | 0x0004 /* SWP_NOZORDER */);
    }
}
