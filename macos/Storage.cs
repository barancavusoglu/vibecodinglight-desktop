using System.Text.Json;
using System.Security.Cryptography;
using AiLight.Core;
namespace AiLight.Mac;
public sealed class Preferences {
    public int Brightness { get; set; } = 65;
    public bool IdleAutoOffEnabled { get; set; }
    public int IdleAutoOffMinutes { get; set; } = 30;
    public string Theme { get; set; } = "dark";
    public string Port { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public Dictionary<string, List<string>> OwnedCommands { get; set; } = [];
}
public static class Storage {
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Root => Environment.GetEnvironmentVariable("AI_LIGHT_MAC_TEST_ROOT") ?? Path.Combine(Home, "Library", "Application Support", "VibeCodingLight");
    public static string IpcRoot => Environment.GetEnvironmentVariable("AI_LIGHT_MAC_TEST_ROOT") is { } test ? Path.Combine(test, "ipc") : Path.Combine(Home, "Library", "Caches", "VibeCodingLight");
    public static string Exe => Environment.ProcessPath!;
    public static void PrivateDirectory(string path) {
        Directory.CreateDirectory(path);
        if (new DirectoryInfo(path).LinkTarget is not null) throw new IOException("Sembolik bağlantı olan veri dizini kullanılmaz.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public static T? Read<T>(string name) {
        try {
            string path = Path.Combine(Root, name);
            if (!File.Exists(path) && Environment.GetEnvironmentVariable("AI_LIGHT_MAC_TEST_ROOT") is null) {
                string previous = Path.Combine(Home, "Library", "Application Support", LegacyBrand.Name, name);
                if (File.Exists(previous) && new FileInfo(previous).LinkTarget is null) {
                    string text = File.ReadAllText(previous);
                    var value = JsonSerializer.Deserialize<T>(text);
                    Atomic(path, text);
                    return value;
                }
            }
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return default; }
    }
    public static Preferences Load() {
        var p = Read<Preferences>("settings.json") ?? new(); p.Brightness = Math.Clamp(p.Brightness, 0, 100);
        p.IdleAutoOffMinutes = SettingSteps.NearestIdleMinutes(p.IdleAutoOffMinutes);
        if (p.Theme is not ("dark" or "light")) p.Theme = "dark";
        return p;
    }
    public static void Save(string name, object value) { PrivateDirectory(Root); Atomic(Path.Combine(Root, name), JsonSerializer.Serialize(value)); }
    public static void Atomic(string path, string text) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temp, text);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, path, true);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static byte[] Key() {
        PrivateDirectory(Root); string path = Path.Combine(Root, "correlation.key");
        try {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(RandomNumberGenerator.GetBytes(32));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        } catch (IOException) when (File.Exists(path)) { }
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length != 32) throw new IOException("Geçersiz korelasyon anahtarı.");
        return bytes;
    }
}
