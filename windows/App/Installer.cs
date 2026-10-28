using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AiLight.App;
public static class Installer {
    private static string Target => Path.Combine(Storage.InstallDir, "AI Light.exe");
    private static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "VibeCodingLight.lnk");
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AI Light";
    public static void StartInstall() {
        // A published self-contained single-file binary can copy itself safely.
        if (File.Exists(Path.ChangeExtension(Storage.Exe, ".runtimeconfig.json"))) throw new InvalidOperationException("Kurulum için dist klasöründeki yayın paketini kullan.");
        if (Path.GetFullPath(Storage.Exe).Equals(Path.GetFullPath(Target), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("AI Light zaten bu bilgisayara kurulu.");
        Process.Start(new ProcessStartInfo(Storage.Exe, "--install") { UseShellExecute = false, CreateNoWindow = true });
    }
    public static async Task Install() {
        if (Path.GetFullPath(Storage.Exe).Equals(Path.GetFullPath(Target), StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(Path.ChangeExtension(Storage.Exe, ".runtimeconfig.json"))) throw new InvalidOperationException("Tek dosyalı yayın paketi gerekli.");
        try { await Bridge.Send("EXIT"); } catch { }
        Directory.CreateDirectory(Storage.InstallDir);
        var temporary = Target + ".new";
        File.Copy(Storage.Exe, temporary, true);
        bool moved = false;
        for (int attempt = 0; attempt < 40; attempt++) {
            try { File.Move(temporary, Target, true); moved = true; break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(250); }
        }
        if (!moved) throw new IOException("Uygulama kapatılamadı. AI Light'tan çıkıp kurucuyu tekrar aç.");
        using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath)) {
            key.SetValue("DisplayName", "VibeCodingLight"); key.SetValue("DisplayVersion", typeof(Installer).Assembly.GetName().Version!.ToString(3));
            key.SetValue("Publisher", "VibeCodingLight"); key.SetValue("InstallLocation", Storage.InstallDir);
            key.SetValue("UninstallString", $"\"{Target}\" --uninstall"); key.SetValue("DisplayIcon", Target);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is not null) {
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(Shortcut); shortcut.TargetPath = Target; shortcut.WorkingDirectory = Storage.InstallDir; shortcut.Save();
            // Remove only our verified legacy shortcut, never a same-named user link.
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "AI Light.lnk");
            if (File.Exists(legacy)) {
                dynamic oldShortcut = shell.CreateShortcut(legacy);
                if (string.Equals((string)oldShortcut.TargetPath, Target, StringComparison.OrdinalIgnoreCase)) File.Delete(legacy);
            }
        }
        // Preserve startup preference, update its path to installed executable.
        var p = Storage.Load();
        if (p.StartAtLogin) {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            run.SetValue("AI Light", $"\"{Target}\" --background");
        }
        Process.Start(new ProcessStartInfo(Target) { UseShellExecute = false, CreateNoWindow = true });
    }
    public static void Uninstall() {
        try {
            var p = Storage.Load();
            foreach (var family in AiLight.Core.HarnessCatalog.Families)
                if (p.OwnedCommands.GetValueOrDefault(family)?.Count > 0) Integrations.Change(p, family, false);
            Storage.Autostart(false);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
            if (File.Exists(Shortcut)) File.Delete(Shortcut);
            try { Bridge.Send("EXIT").GetAwaiter().GetResult(); } catch { }
            var temp = Path.Combine(Path.GetTempPath(), "ai-light-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Storage.Exe, temp);
            Process.Start(new ProcessStartInfo(temp, "--remove-installed " + Environment.ProcessId) { UseShellExecute = false, CreateNoWindow = true });
        } catch (Exception ex) { System.Windows.MessageBox.Show("Kaldırma tamamlanamadı: " + ex.Message, "VibeCodingLight"); }
    }
    public static void RemoveInstalled(int pid) {
        try { using var old = Process.GetProcessById(pid); old.WaitForExit(10000); } catch { }
        for (int i = 0; i < 40; i++) {
            try { if (File.Exists(Target)) File.Delete(Target); break; } catch (IOException) { Thread.Sleep(250); }
        }
        // Never recursively delete the install directory or user data.
        try { if (Directory.Exists(Storage.InstallDir) && !Directory.EnumerateFileSystemEntries(Storage.InstallDir).Any()) Directory.Delete(Storage.InstallDir); } catch { }
    }
}
