using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;

namespace AiLight.Desktop;

// Platform-independent layout and drawing; no device or integration access.
public sealed class AdaptiveCardPanel : Panel
{
    private (int Columns, double Width) Layout(double available)
    {
        if (!double.IsFinite(available)) available = 560;
        available = Math.Max(0, available);
        int columns = Math.Min(Math.Max(1, Children.Count(c => c.IsVisible)), Math.Max(1, (int)Math.Floor((available + 12) / 372)));
        return (columns, Math.Min(560, Math.Max(0, (available - 12 * (columns - 1)) / columns)));
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, width) = Layout(availableSize.Width);
        var heights = new double[columns]; int index = 0;
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            heights[index++ % columns] += child.DesiredSize.Height;
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, heights.Max());
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, width) = Layout(finalSize.Width);
        double left = Math.Max(0, (finalSize.Width - columns * width - 12 * (columns - 1)) / 2);
        var heights = new double[columns]; int index = 0;
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            int column = index++ % columns;
            child.Arrange(new Rect(left + column * (width + 12), heights[column], width, child.DesiredSize.Height));
            heights[column] += child.DesiredSize.Height;
        }
        return finalSize;
    }
    internal void ValidateLayout()
    {
        var children = Children.Where(c => c.IsVisible && c.Bounds.Width > 0).ToArray();
        foreach (var c in children)
        {
            if (c.Bounds.X < -1 || c.Bounds.Right > Bounds.Width + 1) throw new Exception("Card extends outside available width");
            foreach (var other in children.Where(o => o != c))
                if (c.Bounds.Deflate(1).Intersects(other.Bounds.Deflate(1))) throw new Exception("Cards overlap");
        }
    }
}

public sealed class StatusCard : Border
{
    public bool Striped { get; private set; }
    public void SetStripe(bool striped, IBrush fill, Color ink)
    {
        Striped = striped;
        if (!striped) { Background = fill; return; }
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing { Brush = fill, Geometry = new RectangleGeometry(new Rect(0, 0, 18, 18)) });
        var lines = new GeometryGroup();
        foreach (int offset in new[] { -18, 0, 18 }) lines.Children.Add(new LineGeometry { StartPoint = new Point(offset, 18), EndPoint = new Point(offset + 18, 0) });
        drawing.Children.Add(new GeometryDrawing { Pen = new Pen(new SolidColorBrush(Color.FromArgb(33, ink.R, ink.G, ink.B)), 5), Geometry = lines });
        Background = new DrawingBrush { Drawing = drawing, TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 18, 18, RelativeUnit.Absolute), SourceRect = new RelativeRect(0, 0, 18, 18, RelativeUnit.Absolute), Stretch = Stretch.None };
    }
}

public sealed class ColorWheel : Control
{
    private readonly WriteableBitmap bitmap;
    private double hue, saturation = 1;
    private bool dragging;
    public event Action<int, int, int>? ColorSelected;
    public ColorWheel()
    {
        Width = Height = 150; Focusable = true; Cursor = new Cursor(StandardCursorType.Cross);
        ToolTip.SetTip(this, "Bir renk seçmek için tıkla veya sürükle. Ok tuşlarıyla da seçebilirsin.");
        Avalonia.Automation.AutomationProperties.SetName(this, "Renk çarkı");
        const int size = 192; var pixels = new byte[size * size * 4];
        double center = (size - 1) / 2.0;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double dx = x - center, dy = y - center, distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > center) continue;
            var (r, g, b) = Hsv((Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360, distance / center);
            int offset = (y * size + x) * 4;
            pixels[offset] = (byte)b; pixels[offset + 1] = (byte)g; pixels[offset + 2] = (byte)r; pixels[offset + 3] = 255;
        }
        bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        for (int y = 0; y < size; y++) Marshal.Copy(pixels, y * size * 4, buffer.Address + y * buffer.RowBytes, size * 4);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double size = Math.Min(Bounds.Width, Bounds.Height);
        context.DrawImage(bitmap, new Rect(0, 0, size, size));
        var point = new Point(size / 2 + Math.Cos(hue * Math.PI / 180) * saturation * (size / 2 - 1), size / 2 + Math.Sin(hue * Math.PI / 180) * saturation * (size / 2 - 1));
        context.DrawEllipse(null, new Pen(Brushes.Black, 3), point, 7, 7);
        context.DrawEllipse(null, new Pen(Brushes.White, 1.5), point, 7, 7);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); dragging = true; e.Pointer.Capture(this); Select(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (dragging) Select(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (!dragging) return; Select(e.GetPosition(this)); dragging = false; e.Pointer.Capture(null); e.Handled = true; }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); dragging = false; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Right) hue = (hue + (e.Key == Key.Left ? -5 : 5) + 360) % 360;
        else if (e.Key is Key.Up or Key.Down) saturation = Math.Clamp(saturation + (e.Key == Key.Up ? 0.05 : -0.05), 0, 1);
        else return;
        Emit(); e.Handled = true;
    }
    private void Select(Point p)
    {
        double radius = Math.Min(Bounds.Width, Bounds.Height) / 2; if (radius <= 0) return;
        double dx = p.X - radius, dy = p.Y - radius;
        hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360; saturation = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / radius, 0, 1); Emit();
    }
    private void Emit() { var (r, g, b) = Hsv(hue, saturation); ColorSelected?.Invoke(r, g, b); InvalidateVisual(); }
    internal static (int R, int G, int B) Hsv(double hue, double saturation)
    {
        double c = saturation, x = c * (1 - Math.Abs((hue / 60) % 2 - 1)), m = 1 - c;
        (double r, double g, double b) = hue switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
}
