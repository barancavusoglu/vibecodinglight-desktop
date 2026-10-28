using System.IO;
using System.Text.Json;
using AiLight.Core;
namespace AiLight.App;
public static class OpenCodeIntegration {
    private const string Marker = "// VibeCodingLight managed plugin v1";
    public static string LegacyPluginPath => Path.Combine(Path.GetDirectoryName(PluginPath)!, LegacyBrand.PluginFile);
    public static string PluginPath => Path.Combine(
        Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR")
        ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "opencode"),
        "plugins", "vibecodinglight.js");
    private static string Content() {
        using var stream = typeof(OpenCodeIntegration).Assembly.GetManifestResourceStream("AiLight.OpenCodePlugin")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("__AI_LIGHT_EXECUTABLE__", JsonSerializer.Serialize(Storage.Exe));
    }
    public static bool Installed() {
        try { return File.ReadAllText(PluginPath) == Content(); } catch { return false; }
    }
    public static bool Matches(string content) => content == Content();
    public static void Change(Preferences p, bool install) {
        using var mutex = new Mutex(false, "Local\\AiLight.HookConfig.opencode");
        if (!mutex.WaitOne(2000)) throw new IOException("Başka bir kurulum çalışıyor.");
        try {
            string path = PluginPath;
            string? legacy = File.Exists(LegacyPluginPath) ? File.ReadAllText(LegacyPluginPath) : null;
            if (legacy is not null && (new FileInfo(LegacyPluginPath).LinkTarget is not null || !legacy.StartsWith(LegacyBrand.PluginMarker, StringComparison.Ordinal)))
                throw new IOException("Eski konumdaki kullanıcı eklentisi korundu; otomatik taşıma yapılmadı.");
            string? old = File.Exists(path) ? File.ReadAllText(path) : null;
            if (old is not null && !old.StartsWith(Marker, StringComparison.Ordinal) && !old.StartsWith(LegacyBrand.PluginMarker, StringComparison.Ordinal))
                throw new IOException("Aynı adlı farklı bir OpenCode eklentisi var; dosya korundu.");
            if (old is not null) Storage.Atomic(Path.Combine(Storage.Root, "backups", "opencode-" + Guid.NewGuid().ToString("N") + ".js"), old);
            if (legacy is not null) Storage.Atomic(Path.Combine(Storage.Root, "backups", "opencode-legacy-" + Guid.NewGuid().ToString("N") + ".js"), legacy);
            if (install) Storage.Atomic(path, Content());
            else if (old is not null) File.Delete(path); // Only our exact, marked plugin; backup above.
            if (legacy is not null) {
                if (File.ReadAllText(LegacyPluginPath) != legacy) throw new IOException("Eski eklenti değişti; dosya korundu.");
                File.Delete(LegacyPluginPath);
            }
            p.OwnedCommands["opencode"] = install ? [path] : [];
            Storage.Save("settings.json", p);
        } finally { mutex.ReleaseMutex(); }
    }
}
