using AiLight.Core;

namespace AiLight.Desktop;

// Native hosts implement OS facilities; UI and application decisions live here.
public interface IDesktopPlatform : IDisposable {
    string Name { get; }
    bool IsMacOS { get; }
    string Home { get; }
    string DataRoot { get; }
    string ExecutablePath { get; }
    bool IsInstalled { get; }
    DesktopPreferences LoadPreferences();
    T? ReadData<T>(string name);
    void SaveData(string name, object value);
    string IntegrationConfigPath(string family);
    ConnectionCheck CheckConnection(string family, bool enabled);
    bool IntegrationInstalled(DesktopPreferences preferences, string family);
    void ChangeIntegration(DesktopPreferences preferences, string family, bool install);
    bool StartupEnabled(DesktopPreferences preferences);
    string StartupContent { get; }
    void ChangeStartup(DesktopPreferences preferences, bool enable);
    void StartInstall();
    void Uninstall(DesktopPreferences preferences);
    IRobotDiscovery CreateDiscovery();
    string[] Ports();
    bool OwnerAlive(int pid, long started);
    bool TryAcquireInstance();
    Task<bool> Send(string text);
    Task Listen(Action<string> receive, CancellationToken stop);
    int? HandleCommand(string[] args);
    Task<string?> LatestVersion();
    string DownloadPageUrl { get; }
}

public static class DesktopEnvironment {
    public static IDesktopPlatform Platform { get; set; } = null!;
}

public static class Storage {
    public static string Root => DesktopEnvironment.Platform.DataRoot;
    public static string Home => DesktopEnvironment.Platform.Home;
    public static string Exe => DesktopEnvironment.Platform.ExecutablePath;
    public static DesktopPreferences Load() => DesktopEnvironment.Platform.LoadPreferences();
    public static T? Read<T>(string name) => DesktopEnvironment.Platform.ReadData<T>(name);
    public static void Save(string name, object value) => DesktopEnvironment.Platform.SaveData(name, value);
}

public static class Integrations {
    public static readonly string[] Families = HarnessCatalog.Families;
    public static string ConfigPath(string family) => DesktopEnvironment.Platform.IntegrationConfigPath(family);
    public static bool Installed(DesktopPreferences preferences, string family) => DesktopEnvironment.Platform.IntegrationInstalled(preferences, family);
    public static void Change(DesktopPreferences preferences, string family, bool install) => DesktopEnvironment.Platform.ChangeIntegration(preferences, family, install);
}

public static class ConnectionHealth {
    public static ConnectionCheck Check(string family, bool enabled) => DesktopEnvironment.Platform.CheckConnection(family, enabled);
}

public static class StartupService {
    public static bool IsEnabled(DesktopPreferences preferences) => DesktopEnvironment.Platform.StartupEnabled(preferences);
    public static string Content(string exe) => DesktopEnvironment.Platform.StartupContent;
    public static void Change(DesktopPreferences preferences, bool enable) => DesktopEnvironment.Platform.ChangeStartup(preferences, enable);
}

public static class Installer {
    public static bool IsInstalled => DesktopEnvironment.Platform.IsInstalled;
    public static void StartInstall() => DesktopEnvironment.Platform.StartInstall();
    public static void Uninstall(DesktopPreferences preferences) => DesktopEnvironment.Platform.Uninstall(preferences);
}

public sealed class Device() : RobotDevice(DesktopEnvironment.Platform.CreateDiscovery()) {
    public static string[] Ports() => DesktopEnvironment.Platform.Ports();
}

public static class ProcessTree {
    public static bool Alive(int pid, long started) => DesktopEnvironment.Platform.OwnerAlive(pid, started);
}

public sealed record ReleaseInfo(string Version);
public static class UpdateService {
    public static async Task<ReleaseInfo?> Check() => await DesktopEnvironment.Platform.LatestVersion() is { } version ? new(version) : null;
    public static void OpenDownloadPage() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DesktopEnvironment.Platform.DownloadPageUrl) { UseShellExecute = true });
}
