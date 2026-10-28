using System.IO;
using System.Text.Json;
using AiLight.Core;
using Microsoft.Win32;

namespace AiLight.App;

public sealed class Preferences {
    public string Theme { get; set; } = "sage";
    public int Brightness { get; set; } = 65;
    public bool IdleAutoOffEnabled { get; set; }
    public int IdleAutoOffMinutes { get; set; } = 30;
    public string Port { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public bool StartAtLogin { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public Dictionary<string, List<string>> OwnedCommands { get; set; } = [];
}
public static class Storage {
    public static byte[]? CorrelationKey() {
        try {
            string path = Path.Combine(Root, "correlation.key");
            Directory.CreateDirectory(Root);
            if (!File.Exists(path)) {
                try {
                    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    file.Write(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                } catch (IOException) { }
            }
            var bytes = File.ReadAllBytes(path);
            return bytes.Length == 32 ? bytes : null;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
    public static string Root => Environment.GetEnvironmentVariable("AI_LIGHT_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI Light");
    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AI Light");
    public static string Exe => Environment.ProcessPath!;
    public static Preferences Load() {
        var p = Read<Preferences>("settings.json") ?? new();
        p.Brightness = SettingSteps.BrightnessPercent(SettingSteps.BrightnessLevel(p.Brightness));
        p.IdleAutoOffMinutes = SettingSteps.NearestIdleMinutes(p.IdleAutoOffMinutes);
        p.Theme = Themes.Normalize(p.Theme);
        return p;
    }
    public static T? Read<T>(string name) {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Root, name))); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return default; }
    }
    public static void Save(string name, object value) {
        Directory.CreateDirectory(Root);
        Atomic(Path.Combine(Root, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static void Atomic(string path, string text) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(tmp, text, new System.Text.UTF8Encoding(false)); File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static void Autostart(bool enable) {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enable) key.SetValue("AI Light", $"\"{Exe}\" --background");
        else key.DeleteValue("AI Light", false);
    }
}

public static class Integrations {
    public static string ConfigPath(string family) {
        if (HarnessCatalog.IsPreview(family)) return ProviderHooks.PathFor(family, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (family is not ("codex" or "claude")) throw new ArgumentException("Bilinmeyen entegrasyon");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = family == "codex" ? Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(home, ".codex")
            : Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude");
        return Path.Combine(dir, family == "codex" ? "hooks.json" : "settings.json");
    }
    public static void Change(Preferences p, string family, bool install) {
        if (family == "opencode") { OpenCodeIntegration.Change(p, install); return; }
        string path = ConfigPath(family);
        using var mutex = new Mutex(false, "Local\\AiLight.HookConfig." + family);
        if (!mutex.WaitOne(2000)) throw new IOException("Başka bir kurulum çalışıyor.");
        try {
            string? wrapperPath = family == "antigravity" ? ProviderHooks.AntigravityWrapperPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) : null;
            string? wrapper = family == "antigravity" ? ProviderHooks.AntigravityWrapper(Storage.Exe) : null;
            if (install && wrapperPath is not null && File.Exists(wrapperPath)
                && (new FileInfo(wrapperPath).LinkTarget is not null || File.ReadAllText(wrapperPath) != wrapper))
                throw new IOException("Antigravity hook yürütücüsü değiştirilmiş; dosya korundu.");
            if (File.Exists(path) && new FileInfo(path).LinkTarget is not null) throw new IOException("Sembolik bağlantı olan ayar dosyası korundu.");
            string original = File.Exists(path) ? File.ReadAllText(path) : "{}";
            string command = Command(family);
            var owned = p.OwnedCommands.GetValueOrDefault(family) ?? [];
            string edited = HarnessCatalog.IsPreview(family) ? ProviderHooks.Edit(original, command, owned, family, true, install)
                : HookConfig.Edit(original, command, owned, family, install);
            // Journal ownership BEFORE writing config: interrupted installs stay removable.
            var recoverable = owned.Union(install ? new[] { command } : Array.Empty<string>()).ToList();
            p.OwnedCommands[family] = recoverable;
            Storage.Save("settings.json", p);
            if (File.Exists(path)) {
                Directory.CreateDirectory(Path.Combine(Storage.Root, "backups"));
                File.WriteAllText(Path.Combine(Storage.Root, "backups", family + "-" + Guid.NewGuid().ToString("N") + ".json"), original);
            }
            string current = File.Exists(path) ? File.ReadAllText(path) : "{}";
            if (current != original) throw new IOException("Ayar dosyası değişti. Lütfen yeniden dene.");
            if (install && wrapperPath is not null && !File.Exists(wrapperPath)) Storage.Atomic(wrapperPath, wrapper!);
            Storage.Atomic(path, edited);
            p.OwnedCommands[family] = install ? [command] : [];
            Storage.Save("settings.json", p);
        } finally { mutex.ReleaseMutex(); }
    }
    public static bool IsInstalled(Preferences p, string family) {
        if (family == "opencode") return OpenCodeIntegration.Installed();
        try {
            if (family == "antigravity") {
                string wrapperPath = ProviderHooks.AntigravityWrapperPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                if (!File.Exists(wrapperPath) || File.ReadAllText(wrapperPath) != ProviderHooks.AntigravityWrapper(Storage.Exe)) return false;
            }
            return Matches(File.ReadAllText(ConfigPath(family)), family);
        } catch { return false; }
    }
    public static string Command(string family) => HarnessCatalog.IsPreview(family) ? ProviderHooks.Command(Storage.Exe, family, true) : HookConfig.WindowsCommand(Storage.Exe, family);
    public static bool Matches(string content, string family) => HarnessCatalog.IsPreview(family) ? ProviderHooks.Installed(content, Command(family), family, true) : HookConfig.IsInstalled(content, Command(family), family);
}
