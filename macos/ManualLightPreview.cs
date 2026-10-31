using AiLight.Core;

namespace AiLight.Mac;

// Temporary device output never changes session state. A real question/permission
// interrupts brightness preview, matching the Windows application's behavior.
internal sealed class ManualLightPreview(Func<DateTime>? clock = null, Func<double>? chooseHue = null)
{
    private readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
    private readonly Func<double> hue = chooseHue ?? (() => 185 + Random.Shared.NextDouble() * 110);
    private DateTime until;
    private bool brightness;
    public (int R, int G, int B) Color { get; private set; }
    public bool IsActive => now() < until;
    public bool IsBrightness => IsActive && brightness;

    public bool StartBrightness(Activity activity)
    {
        if (activity == Activity.NeedsInput) { Update(activity); return false; }
        var color = IsBrightness ? Color : ColorWheel.Hsv(hue(), 1);
        Start(color.R, color.G, color.B, true);
        return true;
    }

    public void Finish() => until = DateTime.MinValue;

    public void StartColor(int red, int green, int blue) => Start(red, green, blue, false);

    public bool Update(Activity activity)
    {
        if (brightness && activity == Activity.NeedsInput) until = DateTime.MinValue;
        return IsActive;
    }

    private void Start(int red, int green, int blue, bool isBrightness)
    {
        Color = (Math.Clamp(red, 0, 255), Math.Clamp(green, 0, 255), Math.Clamp(blue, 0, 255));
        brightness = isBrightness;
        until = now().AddSeconds(3);
    }
}
