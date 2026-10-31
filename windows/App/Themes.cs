using System.Windows;
using System.Windows.Media;

namespace AiLight.App;
public static class Themes {
    public static readonly string[] All = AiLight.Core.DesktopThemes.All;
    public static string Normalize(string? theme) => AiLight.Core.DesktopThemes.Normalize(theme);
    public static bool IsDark(string theme) => AiLight.Core.DesktopThemes.IsDark(theme);
    public static string SessionColor(string theme,bool waiting) => AiLight.Core.DesktopThemes.SessionColor(theme,waiting);
    public static string Name(string theme) => AiLight.Core.DesktopThemes.Name(theme);
    public static string Description(string theme) => AiLight.Core.DesktopThemes.Description(theme);
    public static string[] Palette(string theme) => AiLight.Core.DesktopThemes.Palette(theme);
    public static void Apply(Window window, string theme) {
        string[] colors = Palette(theme);
        string[] keys = ["Page", "Ink", "Control", "Muted", "Hero", "Card", "Notice", "Edge", "Accent"];
        for (int i = 0; i < keys.Length; i++) {
            var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze(); window.Resources[keys[i]] = brush;
        }
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) {
            int dark = IsDark(theme) ? 1 : 0;
            try { DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
