using System.IO;
using AiLight.Core;
using AiLight.Desktop;
using Microsoft.Win32;

namespace AiLight.App;

public sealed class WindowsDesktopPlatform : IDesktopPlatform {
    private Mutex? instance;
    private bool ownsInstance;
    public string Name => "Windows";
    public bool IsMacOS => false;
    public string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string DataRoot => Storage.Root;
    public string ExecutablePath => Storage.Exe;
    public bool IsInstalled => string.Equals(Path.GetDirectoryName(Storage.Exe), Storage.InstallDir, StringComparison.OrdinalIgnoreCase);
    public DesktopPreferences LoadPreferences() => Storage.Load();
    public T? ReadData<T>(string name) => Storage.Read<T>(name);
    public void SaveData(string name, object value) => Storage.Save(name, value);
    public string IntegrationConfigPath(string family) => family == "opencode" ? OpenCodeIntegration.PluginPath : Integrations.ConfigPath(family);
    public ConnectionCheck CheckConnection(string family, bool enabled) => ConnectionHealth.Check(family, enabled);
    public bool IntegrationInstalled(DesktopPreferences preferences, string family) => Integrations.IsInstalled(preferences, family);
    public void ChangeIntegration(DesktopPreferences preferences, string family, bool install) => Integrations.Change(preferences, family, install);
    public IRobotDiscovery CreateDiscovery() => new RobotDiscovery();
    public string[] Ports() => RobotDiscovery.Ports();
    public bool OwnerAlive(int pid, long started) => ProcessTree.Alive(pid, started);
    public string StartupContent => $"\"{Storage.Exe}\" --background";
    private static bool Isolated => Environment.GetEnvironmentVariable("AI_LIGHT_DATA_DIR") is not null;
    private string? ReadStartup() {
        if (Isolated) return File.Exists(Path.Combine(Storage.Root, "startup-test.txt")) ? File.ReadAllText(Path.Combine(Storage.Root, "startup-test.txt")) : null;
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return run?.GetValue("AI Light") as string;
    }
    private bool OwnsStartup(DesktopPreferences preferences, string value) => value == preferences.StartupRegistration || value == StartupContent
        || value == $"\"{Path.Combine(Storage.InstallDir, "AI Light.exe")}\" --background";
    public bool StartupEnabled(DesktopPreferences preferences) => ReadStartup() is { } value && OwnsStartup(preferences, value);
    public void ChangeStartup(DesktopPreferences preferences, bool enable) {
        string? old = ReadStartup();
        if (old is not null && !OwnsStartup(preferences, old)) throw new IOException("Başlangıç kaydı kullanıcı tarafından değiştirilmiş; kayıt korundu.");
        if (Isolated) {
            string path = Path.Combine(Storage.Root, "startup-test.txt");
            if (enable) Storage.Atomic(path, StartupContent); else if (File.Exists(path)) File.Delete(path);
        } else {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enable) run.SetValue("AI Light", StartupContent); else if (old is not null) run.DeleteValue("AI Light", false);
        }
        preferences.StartAtLogin = enable; preferences.StartupRegistration = enable ? StartupContent : "";
        Storage.Save("settings.json", preferences);
    }
    public void StartInstall() => Installer.StartInstall();
    public void Uninstall(DesktopPreferences preferences) => Installer.Uninstall();
    public bool TryAcquireInstance() {
        instance = new Mutex(false, "Local\\" + Bridge.PipeName);
        for (int attempt = 0; attempt < 80; attempt++) {
            try { if (instance.WaitOne(0)) { ownsInstance = true; return true; } }
            catch (AbandonedMutexException) { ownsInstance = true; return true; }
            if (Send("SHOW").GetAwaiter().GetResult()) return false;
            Thread.Sleep(100);
        }
        throw new IOException("Önceki uygulama henüz kapanmadı. Birkaç saniye sonra yeniden aç.");
    }
    public async Task<bool> Send(string text) { try { await Bridge.Send(text); return true; } catch { return false; } }
    public Task Listen(Action<string> receive, CancellationToken stop) => Bridge.Listen(receive, stop);
    public async Task<string?> LatestVersion() => (await UpdateService.Check())?.Version;
    public string DownloadPageUrl => UpdateService.DownloadPageUrl;
    public int? HandleCommand(string[] args) {
        string? command = args.FirstOrDefault();
        if (command == "--hook") { if (args.Length is 2 or 3) Bridge.Hook(args[1], args.Length == 3 ? args[2] : "").GetAwaiter().GetResult(); return 0; }
        if (command == "--integration-smoke" && args.Length == 2) return SmokeTest.Run(args[1]).GetAwaiter().GetResult();
        if (command == "--self-test") return PlatformChecks.Run(this);
        if (command == "--quit") return Send("EXIT").GetAwaiter().GetResult() ? 0 : 1;
        try {
            if (command == "--install" || Path.GetFileName(Storage.Exe).Equals("VibeCodingLight-Setup.exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(Storage.Exe).Equals("AI-Light-Setup.exe", StringComparison.OrdinalIgnoreCase)) { Installer.Install().GetAwaiter().GetResult(); return 0; }
            if (command == "--uninstall") { Installer.Uninstall(); return 0; }
            if (command == "--remove-installed" && args.Length == 2 && int.TryParse(args[1], out int pid)) { Installer.RemoveInstalled(pid); return 0; }
        } catch (Exception error) { System.Windows.Forms.MessageBox.Show(error.Message, "VibeCodingLight"); return 1; }
        return null;
    }
    public void Dispose() { if (ownsInstance) { ownsInstance = false; instance?.ReleaseMutex(); } instance?.Dispose(); instance = null; }
}
