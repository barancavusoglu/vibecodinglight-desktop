using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace AiLight.App;

// All dimensions are WPF device-independent units, scaled by the monitor DPI.
// Cards share the available width; columns disappear before cards become narrow.
public sealed class AdaptiveCardPanel : Panel {
    private const double ComfortableWidth = 360;
    private const double MaximumWidth = 560;
    private const double Gap = 12;
    private (int Columns, double Width) Layout(double available) {
        if (!double.IsFinite(available)) available = MaximumWidth;
        available = Math.Max(0, available);
        int columns = Math.Max(1, (int)Math.Floor((available + Gap) / (ComfortableWidth + Gap)));
        columns = Math.Min(columns, Math.Max(1, InternalChildren.Cast<UIElement>().Count(c => c.Visibility != Visibility.Collapsed)));
        return (columns, Math.Min(MaximumWidth, Math.Max(0, (available - Gap * (columns - 1)) / columns)));
    }
    protected override Size MeasureOverride(Size availableSize) {
        var (columns, width) = Layout(availableSize.Width);
        var heights = new double[columns];
        int index = 0;
        foreach (UIElement child in InternalChildren) {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(width, double.PositiveInfinity));
            heights[index++ % columns] += child.DesiredSize.Height;
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, heights.Max());
    }
    protected override Size ArrangeOverride(Size finalSize) {
        var (columns, width) = Layout(finalSize.Width);
        double left = Math.Max(0, (finalSize.Width - (columns * width + Gap * (columns - 1))) / 2);
        var heights = new double[columns];
        int index = 0;
        foreach (UIElement child in InternalChildren) {
            if (child.Visibility == Visibility.Collapsed) continue;
            int column = index++ % columns;
            child.Arrange(new Rect(left + column * (width + Gap), heights[column], width, child.DesiredSize.Height));
            heights[column] += child.DesiredSize.Height;
        }
        return finalSize;
    }
}
