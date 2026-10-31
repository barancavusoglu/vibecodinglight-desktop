namespace AiLight.Core;

// Persisted names stay compatible with both existing installations.
public class DesktopPreferences {
    public string Theme { get; set; } = DesktopThemes.Default;
    public int Brightness { get; set; } = 65;
    public bool IdleAutoOffEnabled { get; set; }
    public int IdleAutoOffMinutes { get; set; } = 30;
    public bool StartAtLogin { get; set; }
    public string StartupRegistration { get; set; } = "";
    public bool AutoCheckUpdates { get; set; } = true;
    public string Port { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public Dictionary<string, List<string>> OwnedCommands { get; set; } = [];
}

public record ConnectionCheck(string Family, bool Enabled, bool Healthy, string Fingerprint, string Detail);
