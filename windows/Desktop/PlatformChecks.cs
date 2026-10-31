using System.IO;
using AiLight.Core;

namespace AiLight.App;

internal static class PlatformChecks {
    public static int Run(WindowsDesktopPlatform platform) {
        string root = Path.Combine(Path.GetTempPath(), "vibecodinglight-platform-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AI_LIGHT_DATA_DIR", root);
        try {
            var preferences = new DesktopPreferences { Theme = "night", Brightness = 70, Port = "COM-test", DeviceId = "saved-robot", AutoCheckUpdates = false };
            platform.SaveData("settings.json", preferences);
            var loaded = platform.LoadPreferences();
            Check(loaded.Theme == "night" && loaded.Brightness == 70 && loaded.Port == "COM-test" && loaded.DeviceId == "saved-robot" && !loaded.AutoCheckUpdates, "Existing Windows preferences survive shared runtime loading");
            platform.ChangeStartup(loaded, true);
            Check(platform.StartupEnabled(loaded) && loaded.StartAtLogin, "Shared startup opt-in uses the Windows adapter");
            platform.ChangeStartup(loaded, false);
            Check(!platform.StartupEnabled(loaded) && !loaded.StartAtLogin, "Shared startup opt-out removes only the owned entry");
            string path = Path.Combine(root, "startup-test.txt");
            File.WriteAllText(path, "user-owned-command");
            bool refused = false;
            try { platform.ChangeStartup(loaded, false); } catch (IOException) { refused = true; }
            Check(refused && File.ReadAllText(path) == "user-owned-command", "Foreign startup registration is preserved");
            Check(platform.IntegrationConfigPath("opencode") == OpenCodeIntegration.PluginPath && platform.DownloadPageUrl == "https://www.vibecodinglight.com/#setup", "Native integration and update destinations are preserved");
            Console.WriteLine("5 Windows platform checks passed."); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        // The fixture is retained under its own GUID for failure inspection.
    }
    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); Console.WriteLine("PASS " + message); }
}
