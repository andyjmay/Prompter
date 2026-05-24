using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views;

public partial class RecordingOverlay : Window
{
    private readonly OverlayPlacementConfig _placement;

    public RecordingOverlay(OverlayPlacementConfig placement, OverlayStyleConfig style)
    {
        InitializeComponent();
        _placement = placement;
        Loaded += OnLoaded;

        var brushes = ThemeResolver.Resolve(style);
        RootBorder.Background = brushes.OverlayBackground;
        RootBorder.BorderBrush = brushes.OverlayBorder;
        PulseDot.Fill = brushes.Accent;
        StatusText.Foreground = brushes.PrimaryText;
        AudioMeter.Foreground = brushes.Accent;
        ProcessingBar.Foreground = brushes.ProcessingAccent;

        if (placement.ShowAudioLevelMeter)
        {
            AudioMeter.Visibility = Visibility.Visible;
        }
    }

    public void UpdateAudioLevel(double level)
    {
        AudioMeter.Value = level;
    }

    public void TransitionToProcessing()
    {
        if (Resources["PulseStoryboard"] is Storyboard sb)
        {
            sb.Stop();
        }

        PulseDot.Visibility = Visibility.Collapsed;
        AudioMeter.Visibility = Visibility.Collapsed;
        ProcessingBar.Visibility = Visibility.Visible;
        StatusText.Text = "Processing…";
        StatusText.Foreground = ProcessingBar.Foreground;
    }

    public void UpdateProcessingStage(string label)
    {
        StatusText.Text = label;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var (left, top) = OverlayPositioner.ComputePosition(
            this,
            _placement.Anchor,
            _placement.OffsetX,
            _placement.OffsetY);
        Left = left;
        Top = top;

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
