using System.Windows;
using System.Windows.Media;

namespace AiLight.App;
public static class Themes {
    public static readonly string[] All = ["sage", "dark", "mist", "sand", "forest", "night"];
    public static string Normalize(string? theme) => theme is "ocean" or "lavender" ? "mist" : All.Contains(theme) ? theme! : "sage";
    public static bool IsDark(string theme) => Normalize(theme) is "dark" or "forest" or "night";
    public static string SessionColor(string theme, bool waiting) => Normalize(theme) == "mist"
        ? (waiting ? "#78231C" : "#24502D")
        : IsDark(theme) ? (waiting ? "#FF9C89" : "#A8D88C") : (waiting ? "#A33C2C" : "#32633C");
    public static string Name(string theme) => Normalize(theme) switch { "dark" => "Koyu", "mist" => "Duman", "sand" => "Kum", "forest" => "Orman", "night" => "Gece", _ => "Açık" };
    public static string Description(string theme) => Normalize(theme) switch { "dark" => "Sade kömür", "mist" => "Dengeli gri", "sand" => "Sıcak ve yumuşak", "forest" => "Derin yeşil", "night" => "Sessiz lacivert", _ => "Ferah ve sade" };
    public static string[] Palette(string theme) => Normalize(theme) switch {
        "dark" => ["#161719", "#F3F3F5", "#333438", "#B7B8BF", "#25272B", "#222326", "#F5B78D", "#3E4046", "#2E70CA"],
        "mist" => ["#90969D", "#182029", "#A2A9B0", "#25303B", "#ADB4BB", "#B4BAC0", "#71320E", "#6D7680", "#344F70"],
        "sand" => ["#EEE6D9", "#332D25", "#E4D8C5", "#675B4D", "#E7DDCE", "#FBF5E9", "#8A401B", "#CCBDA5", "#85552C"],
        "forest" => ["#172420", "#EDF3E8", "#30463A", "#B5C9B8", "#2C4033", "#23352D", "#E7BD91", "#47634F", "#4C7858"],
        "night" => ["#1E2234", "#EEF0FF", "#353E59", "#BAC2DD", "#303852", "#2B3149", "#F1BF9E", "#4A5574", "#5669A7"],
        _ => ["#F5F5F7", "#202124", "#E8E8EC", "#62636C", "#EEEEF2", "#FFFFFF", "#94502B", "#DEDEE4", "#0865C9"]
    };
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
