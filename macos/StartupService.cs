using System.Xml.Linq;
using AiLight.Core;
namespace AiLight.Mac;

public static class StartupService
{
    public static string PathName => Path.Combine(Environment.GetEnvironmentVariable("AI_LIGHT_MAC_TEST_ROOT") ?? Storage.Home, "Library", "LaunchAgents", "com.vibecodinglight.desktop.plist");
    public static string Content(string exe) => new XDocument(new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
        new XElement("key", "Label"), new XElement("string", "com.vibecodinglight.desktop"),
        new XElement("key", "ProgramArguments"), new XElement("array", new XElement("string", exe), new XElement("string", "--background")),
        new XElement("key", "RunAtLoad"), new XElement("true"),
        new XElement("key", "LimitLoadToSessionType"), new XElement("string", "Aqua")))).ToString();
    public static bool IsEnabled(DesktopPreferences p)
    {
        try { return File.Exists(PathName) && new FileInfo(PathName).LinkTarget is null && new FileInfo(PathName).Length < 16384 && File.ReadAllText(PathName) == p.StartupRegistration && p.StartupRegistration.Length > 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public static void Change(DesktopPreferences p, bool enable)
    {
        if (enable && Environment.GetEnvironmentVariable("AI_LIGHT_MAC_TEST_ROOT") is null && Installer.Bundle is null)
            throw new InvalidOperationException("Otomatik başlatma için yayınlanmış .app paketini kullan.");
        if (File.Exists(PathName) && new FileInfo(PathName).LinkTarget is not null) throw new IOException("Kullanıcının sembolik bağlantısı korundu.");
        if (File.Exists(PathName) && new FileInfo(PathName).Length > 16384) throw new IOException("Mevcut giriş öğesi korundu.");
        string? old = File.Exists(PathName) ? File.ReadAllText(PathName) : null;
        string next = Content(Storage.Exe);
        if (old is not null && old != p.StartupRegistration && old != next) throw new IOException("Giriş öğesi kullanıcı tarafından değiştirilmiş; kayıt korundu.");
        if (old is not null) Storage.Atomic(Path.Combine(Storage.Root, "backups", "startup-" + Guid.NewGuid().ToString("N") + ".plist"), old);
        if (enable) Storage.Atomic(PathName, next); else if (old is not null) File.Delete(PathName);
        p.StartAtLogin = enable; p.StartupRegistration = enable ? next : ""; Storage.Save("settings.json", p);
    }
}
