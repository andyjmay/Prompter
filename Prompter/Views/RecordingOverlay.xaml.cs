using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views;

public partial class RecordingOverlay : Window
{
    private readonly OverlayPlacementConfig _placement;
    private readonly OverlayStyleConfig _style;
    private readonly bool _compact;
    private readonly string _listeningLabel;
    private readonly string _processingLabel;

    public RecordingOverlay(OverlayPlacementConfig placement, OverlayStyleConfig style)
    {
        InitializeComponent();
        _placement = placement;
        _style = style;
        _compact = !style.ShowStatusText;
        _listeningLabel = style.ListeningLabel;
        _processingLabel = style.ProcessingLabel;

        Loaded += OnLoaded;

        var brushes = ThemeResolver.Resolve(style);
        RootBorder.Background = brushes.OverlayBackground;
        RootBorder.BorderBrush = brushes.OverlayBorder;
        RootBorder.CornerRadius = new CornerRadius(style.CornerRadius);
        RootBorder.Padding = new Thickness(style.Padding);

        if (style.ShadowEnabled)
        {
            RootBorder.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.3,
                BlurRadius = 12,
                ShadowDepth = 4
            };
        }

        PulseDot.Fill = brushes.Accent;
        StatusText.Foreground = brushes.PrimaryText;
        StatusText.FontFamily = new System.Windows.Media.FontFamily(style.FontFamily);
        StatusText.FontSize = style.OverlayFontSize;
        StatusText.Text = _listeningLabel;

        AudioMeter.Foreground = brushes.Accent;
        ProcessingBar.Foreground = brushes.ProcessingAccent;

        if (_compact)
        {
            StatusText.Visibility = Visibility.Collapsed;
            PulseDot.Margin = new Thickness(0);
            AudioMeter.Visibility = Visibility.Collapsed;
            ProcessingBar.Visibility = Visibility.Collapsed;
        }
        else if (placement.ShowAudioLevelMeter)
        {
            AudioMeter.Visibility = Visibility.Visible;
        }

        // Set pulse speed
        if (Resources["PulseStoryboard"] is Storyboard sb)
        {
            var duration = style.PulseSpeed switch
            {
                OverlayPulseSpeed.Slow => TimeSpan.FromSeconds(1.2),
                OverlayPulseSpeed.Fast => TimeSpan.FromSeconds(0.4),
                _ => TimeSpan.FromSeconds(0.8)
            };
            foreach (var child in sb.Children)
            {
                if (child is DoubleAnimation anim)
                {
                    anim.Duration = new Duration(duration);
                }
            }
        }
    }

    public void UpdateAudioLevel(double level)
    {
        if (!_compact)
        {
            AudioMeter.Value = level;
        }
    }

    public void TransitionToProcessing()
    {
        if (_compact)
        {
            var brushes = ThemeResolver.Resolve(_style);
            PulseDot.Fill = brushes.ProcessingAccent;
            return;
        }

        if (Resources["PulseStoryboard"] is Storyboard sb)
        {
            sb.Stop();
        }

        PulseDot.Visibility = Visibility.Collapsed;
        AudioMeter.Visibility = Visibility.Collapsed;
        ProcessingBar.Visibility = Visibility.Visible;
        StatusText.Text = _processingLabel;
        StatusText.Foreground = ProcessingBar.Foreground;
    }

    public void UpdateProcessingStage(string label)
    {
        if (!_compact)
        {
            StatusText.Text = label;
        }
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
