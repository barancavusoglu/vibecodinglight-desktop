using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AiLight.App;

public sealed class ColorSelectedEventArgs(int r, int g, int b) : EventArgs {
    public int R { get; } = r;
    public int G { get; } = g;
    public int B { get; } = b;
}

public sealed class ColorWheel : FrameworkElement {
    public event EventHandler<ColorSelectedEventArgs>? PreviewColorChanged;

    private WriteableBitmap? bitmap;
    private int bitmapSize;
    private double hue;
    private double saturation = 1;

    public ColorWheel() {
        Focusable = true;
        Cursor = System.Windows.Input.Cursors.Cross;
        ToolTip = "Bir renk seçmek için tıkla veya sürükle";
        SnapsToDevicePixels = true;
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) {
        double width = double.IsInfinity(availableSize.Width) ? 168 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? 168 : availableSize.Height;
        return new System.Windows.Size(Math.Min(width, 168), Math.Min(height, 168));
    }

    protected override void OnRender(DrawingContext drawingContext) {
        base.OnRender(drawingContext);
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        int pixels = Math.Max(1, (int)Math.Ceiling(size));
        if (bitmap is null || bitmapSize != pixels) CreateBitmap(pixels);

        double left = (ActualWidth - size) / 2;
        double top = (ActualHeight - size) / 2;
        drawingContext.DrawImage(bitmap, new Rect(left, top, size, size));

        double radius = Math.Max(0, size / 2 - 1);
        double radians = hue * Math.PI / 180;
        var marker = new System.Windows.Point(
            left + size / 2 + Math.Cos(radians) * saturation * radius,
            top + size / 2 + Math.Sin(radians) * saturation * radius);
        drawingContext.DrawEllipse(System.Windows.Media.Brushes.Transparent, new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, 3), marker, 7, 7);
        drawingContext.DrawEllipse(System.Windows.Media.Brushes.Transparent, new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 1.5), marker, 7, 7);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        Select(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e) {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            Select(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        Select(e.GetPosition(this));
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Select(System.Windows.Point point) {
        double size = Math.Min(ActualWidth, ActualHeight);
        double radius = size / 2;
        if (radius <= 0) return;
        double left = (ActualWidth - size) / 2;
        double top = (ActualHeight - size) / 2;
        double dx = point.X - left - radius;
        double dy = point.Y - top - radius;
        saturation = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / radius, 0, 1);
        hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        var rgb = HsvToRgb(hue, saturation, 1);
        var args = new ColorSelectedEventArgs(rgb.R, rgb.G, rgb.B);
        PreviewColorChanged?.Invoke(this, args);
        InvalidateVisual();
    }

    private void CreateBitmap(int size) {
        bitmapSize = size;
        var pixels = new byte[size * size * 4];
        double center = (size - 1) / 2.0;
        double radius = Math.Max(1, center);

        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
            double dx = x - center, dy = y - center;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            int offset = (y * size + x) * 4;
            if (distance > radius) continue;

            double pixelHue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            var rgb = HsvToRgb(pixelHue, distance / radius, 1);
            pixels[offset] = (byte)rgb.B;
            pixels[offset + 1] = (byte)rgb.G;
            pixels[offset + 2] = (byte)rgb.R;
            pixels[offset + 3] = 255;
        }

        bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bitmap.Freeze();
    }

    internal static (int R, int G, int B) HsvToRgb(double hue, double saturation, double value) {
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs((hue / 60) % 2 - 1));
        double m = value - chroma;
        (double r, double g, double b) = hue switch {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
}
