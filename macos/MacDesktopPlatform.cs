using System.Net.Sockets;
using AiLight.Core;
using AiLight.Desktop;

namespace AiLight.Mac;

public sealed class MacDesktopPlatform : IDesktopPlatform {
    private FileStream? lifetimeLock;
    private Socket? listener;
    public string Name => "macOS";
    public bool IsMacOS => true;
    public string Home => Storage.Home;
    public string DataRoot => Storage.Root;
    public string ExecutablePath => Storage.Exe;
    public bool IsInstalled => Installer.IsInstalled;
    public DesktopPreferences LoadPreferences() => Storage.Load();
    public T? ReadData<T>(string name) => Storage.Read<T>(name);
    public void SaveData(string name, object value) => Storage.Save(name, value);
    public string IntegrationConfigPath(string family) => Integrations.ConfigPath(family);
    public ConnectionCheck CheckConnection(string family, bool enabled) => ConnectionHealth.Check(family, enabled);
    public bool IntegrationInstalled(DesktopPreferences preferences, string family) => Integrations.Installed(preferences, family);
    public void ChangeIntegration(DesktopPreferences preferences, string family, bool install) => Integrations.Change(preferences, family, install);
    public bool StartupEnabled(DesktopPreferences preferences) => StartupService.IsEnabled(preferences);
    public string StartupContent => StartupService.Content(Storage.Exe);
    public void ChangeStartup(DesktopPreferences preferences, bool enable) => StartupService.Change(preferences, enable);
    public void StartInstall() => Installer.StartInstall();
    public void Uninstall(DesktopPreferences preferences) => Installer.Uninstall(preferences);
    public IRobotDiscovery CreateDiscovery() => new RobotDiscovery();
    public string[] Ports() => RobotDiscovery.Ports();
    public bool OwnerAlive(int pid, long started) => ProcessTree.Alive(pid, started);
    public Task<bool> Send(string text) => Bridge.Send(text);
    public Task Listen(Action<string> receive, CancellationToken stop) => Bridge.Listen(listener ?? throw new InvalidOperationException("IPC not bound."), receive, stop);
    public async Task<string?> LatestVersion() => (await UpdateService.Check())?.Version;
    public string DownloadPageUrl => UpdateService.DownloadPageUrl;
    public bool TryAcquireInstance() {
        Storage.PrivateDirectory(Storage.IpcRoot);
        for (int attempt = 0; attempt < 80; attempt++) {
            if (Bridge.Send("SHOW").GetAwaiter().GetResult()) return false;
            try { lifetimeLock = new FileStream(Path.Combine(Storage.IpcRoot, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Thread.Sleep(100); continue; }
            listener = Bridge.Bind(); return true;
        }
        throw new IOException("Önceki uygulama henüz kapanmadı. Birkaç saniye sonra yeniden aç.");
    }
    public int? HandleCommand(string[] args) {
        if (args.FirstOrDefault() == "--hook") { if (args.Length is 2 or 3) Bridge.Hook(args[1], args.Length == 3 ? args[2] : "").GetAwaiter().GetResult(); return 0; }
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--quit")) return Bridge.Send("EXIT").GetAwaiter().GetResult() ? 0 : 1;
        if (args.Contains("--uninstall")) {
            try { Installer.Uninstall(Storage.Load()); Bridge.Send("EXIT").GetAwaiter().GetResult(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
        return null;
    }
    public void Dispose() {
        listener?.Dispose(); listener = null;
        if (lifetimeLock is not null) { try { File.Delete(Bridge.PathName); } catch { } lifetimeLock.Dispose(); lifetimeLock = null; }
    }
}
