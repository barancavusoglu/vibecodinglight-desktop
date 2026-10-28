using System.Text.Json;
using AiLight.Core;
namespace AiLight.Mac;
public static class Integrations {
    public static readonly string[] Families = HarnessCatalog.Families;
    public static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
    public static string Command(string exe, string family) {
        if (!Families.Contains(family)) throw new ArgumentException("Bilinmeyen entegrasyon");
        return Quote(exe) + " --hook " + family;
    }
    public static string ConfigPath(string family) => family switch {
        "codex" => Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Storage.Home, ".codex"), "hooks.json"),
        "claude" => Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Storage.Home, ".claude"), "settings.json"),
        "opencode" => Path.Combine(Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR") ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Storage.Home, ".config"), "opencode"), "plugins", "vibecodinglight.js"),
        "copilot" or "antigravity" => ProviderHooks.PathFor(family, Environment.GetEnvironmentVariable("AI_LIGHT_MAC_TEST_ROOT") ?? Storage.Home),
        _ => throw new ArgumentException("Bilinmeyen entegrasyon")
    };
    public static string Plugin(string exe) {
        using var reader = new StreamReader(typeof(Integrations).Assembly.GetManifestResourceStream("AiLight.OpenCodePlugin")!);
        return reader.ReadToEnd().Replace("__AI_LIGHT_EXECUTABLE__", JsonSerializer.Serialize(exe));
    }
    public static bool Installed(Preferences p, string family) {
        try {
            string text = File.ReadAllText(ConfigPath(family));
            if (family == "opencode") return text == Plugin(Storage.Exe);
            if (HarnessCatalog.IsPreview(family)) return ProviderHooks.Installed(text, Command(Storage.Exe, family), family, false);
            return HookConfig.IsInstalled(text, Command(Storage.Exe, family), family);
        } catch { return false; }
    }
    public static void Change(Preferences p, string family, bool install) {
        string path = ConfigPath(family);
        Directory.CreateDirectory(Storage.Root);
        using var installLock = new FileStream(Path.Combine(Storage.Root, "hooks-" + family + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path) && new FileInfo(path).LinkTarget is not null) throw new IOException("Sembolik bağlantı olan ayar dosyası korundu.");
        string? old = File.Exists(path) ? File.ReadAllText(path) : null;
        string next;
        var owned = p.OwnedCommands.GetValueOrDefault(family) ?? [];
        string command = Command(Storage.Exe, family);
        if (family == "opencode") {
            if (old is not null && !old.StartsWith("// VibeCodingLight managed plugin v1", StringComparison.Ordinal) && !old.StartsWith(LegacyBrand.PluginMarker, StringComparison.Ordinal)) throw new IOException("Aynı adlı kullanıcı eklentisi korundu.");
            next = Plugin(Storage.Exe);
        } else next = HarnessCatalog.IsPreview(family) ? ProviderHooks.Edit(old ?? "{}", command, owned, family, false, install)
            : HookConfig.Edit(old ?? "{}", command, owned, family, install);
        string legacyPath = Path.Combine(Path.GetDirectoryName(path)!, LegacyBrand.PluginFile);
        string? legacy = family == "opencode" && File.Exists(legacyPath) ? File.ReadAllText(legacyPath) : null;
        if (legacy is not null && (new FileInfo(legacyPath).LinkTarget is not null || !legacy.StartsWith(LegacyBrand.PluginMarker, StringComparison.Ordinal))) throw new IOException("Eski konumdaki kullanıcı eklentisi korundu.");
        if (legacy is not null) Storage.Atomic(Path.Combine(Storage.Root, "backups", "opencode-legacy-" + Guid.NewGuid().ToString("N") + ".bak"), legacy);
        if (old is not null) Storage.Atomic(Path.Combine(Storage.Root, "backups", family + "-" + Guid.NewGuid().ToString("N") + ".bak"), old);
        p.OwnedCommands[family] = owned.Union(install ? new[] { command } : Array.Empty<string>()).ToList(); Storage.Save("settings.json", p);
        if ((File.Exists(path) ? File.ReadAllText(path) : null) != old) throw new IOException("Dosya değişti; yeniden dene.");
        if (!install && family == "opencode") { if (old is not null) File.Delete(path); }
        else Storage.Atomic(path, next);
        if (legacy is not null) {
            if (File.ReadAllText(legacyPath) != legacy) throw new IOException("Eski eklenti değişti; dosya korundu.");
            File.Delete(legacyPath);
        }
        p.OwnedCommands[family] = install ? [command] : []; Storage.Save("settings.json", p);
    }
}
