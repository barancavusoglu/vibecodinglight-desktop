using AiLight.Core;
namespace AiLight.Mac;
internal static class MacThemes {
    public static readonly string[] All = DesktopThemes.All;
    public static string Normalize(string? theme) => DesktopThemes.Normalize(theme);
    public static bool IsDark(string theme) => DesktopThemes.IsDark(theme);
    public static string Name(string theme) => DesktopThemes.Name(theme);
    public static string Description(string theme) => DesktopThemes.Description(theme);
    public static string[] Palette(string theme) => DesktopThemes.Palette(theme);
    public static string SessionColor(string theme,bool waiting) => DesktopThemes.SessionColor(theme,waiting);
}
