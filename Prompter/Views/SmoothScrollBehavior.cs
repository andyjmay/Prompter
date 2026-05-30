using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Prompter.Views;

public static class SmoothScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        if ((bool)e.NewValue)
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        else
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not UIElement source) return;

        var targetScrollViewer = GetTargetScrollViewer(source);
        if (targetScrollViewer == null) return;

        var hitElement = e.OriginalSource as DependencyObject;
        if (hitElement == null) return;

        var firstScrollViewer = FindFirstScrollViewer(hitElement);
        if (firstScrollViewer == null) return;

        // If the element under the mouse has its own scroll viewer and it is not
        // the one we are attached to, let it handle the wheel first.
        if (firstScrollViewer != targetScrollViewer)
        {
            if (CanScroll(firstScrollViewer, e.Delta))
            {
                // The nested scroll viewer is not at boundary and will handle the
                // MouseWheel event (bubbling). We must not mark this preview
                // as handled so the bubbling event is raised.
                return;
            }

            // Nested scroll viewer is at boundary. Scroll the next scroll viewer
            // up the tree instead.
            var nextScrollViewer = FindNextScrollViewer(firstScrollViewer);
            if (nextScrollViewer != null)
            {
                ScrollBy(nextScrollViewer, e.Delta);
                e.Handled = true;
            }

            return;
        }

        // The element under the mouse maps directly to our target scroll viewer.
        if (!CanScroll(targetScrollViewer, e.Delta))
        {
            // At boundary – let the parent scroll viewer handle it.
            return;
        }

        ScrollBy(targetScrollViewer, e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? GetTargetScrollViewer(UIElement element)
    {
        if (element is ScrollViewer sv) return sv;
        if (element is ItemsControl itemsControl) return FindVisualChild<ScrollViewer>(itemsControl);
        if (element is TextBoxBase textBox) return FindVisualChild<ScrollViewer>(textBox);
        return null;
    }

    private static ScrollViewer? FindFirstScrollViewer(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is ScrollViewer sv) return sv;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static ScrollViewer? FindNextScrollViewer(ScrollViewer current)
    {
        var parent = VisualTreeHelper.GetParent(current);
        if (parent == null) return null;
        return FindFirstScrollViewer(parent);
    }

    private static bool CanScroll(ScrollViewer scrollViewer, int delta)
    {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (shift && scrollViewer.ScrollableWidth > 0)
        {
            bool scrollingLeft = delta > 0;
            bool scrollingRight = delta < 0;
            bool atLeft = scrollViewer.HorizontalOffset <= 0;
            bool atRight = scrollViewer.HorizontalOffset >= scrollViewer.ScrollableWidth;
            return !((scrollingLeft && atLeft) || (scrollingRight && atRight));
        }

        if (scrollViewer.ScrollableHeight <= 0) return false;

        bool scrollingUp = delta > 0;
        bool scrollingDown = delta < 0;
        bool atTop = scrollViewer.VerticalOffset <= 0;
        bool atBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight;
        return !((scrollingUp && atTop) || (scrollingDown && atBottom));
    }

    private static void ScrollBy(ScrollViewer scrollViewer, int delta)
    {
        const double pixelsPerNotch = 24;
        double offsetDelta = -(delta / 120.0) * pixelsPerNotch;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && scrollViewer.ScrollableWidth > 0)
        {
            double newOffset = scrollViewer.HorizontalOffset + offsetDelta;
            newOffset = Math.Max(0, Math.Min(newOffset, scrollViewer.ScrollableWidth));
            scrollViewer.ScrollToHorizontalOffset(newOffset);
        }
        else
        {
            double newOffset = scrollViewer.VerticalOffset + offsetDelta;
            newOffset = Math.Max(0, Math.Min(newOffset, scrollViewer.ScrollableHeight));
            scrollViewer.ScrollToVerticalOffset(newOffset);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }
}
