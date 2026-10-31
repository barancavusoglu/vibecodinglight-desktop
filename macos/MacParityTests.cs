using System.Net;
using System.Net.Http;
using System.Text.Json;
using AiLight.Core;
namespace AiLight.Mac;

internal static class MacParityTests
{
    public static async Task Run(Action<bool, string> check, string root, Preferences original)
    {
        string keyPath=Path.Combine(Storage.Root,"correlation.key");
        byte[]? oldKey=File.Exists(keyPath)?File.ReadAllBytes(keyPath):null;
        try{File.WriteAllBytes(keyPath,[1]);check(Storage.CorrelationKey() is null,"Unreadable/corrupt correlation key degrades matching without dropping all hook events");}
        finally{if(oldKey is null)File.Delete(keyPath);else File.WriteAllBytes(keyPath,oldKey);}
        var prefs = new Preferences();
        foreach (string family in new[] { "codex", "claude" })
        {
            string path = Integrations.ConfigPath(family), command = Integrations.Command(Storage.Exe, family);
            string foreign = "{\"theme\":\"user\",\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"user-tool\"}]}]}}";
            string old = Integrations.Command("/Applications/Old VibeCodingLight.app/Contents/MacOS/VibeCodingLight", family);
            Storage.Atomic(path, HookConfig.Edit(foreign, old, [], family, true)); prefs.OwnedCommands[family] = [old];
            Integrations.Change(prefs, family, true);
            check(Integrations.Installed(prefs, family) && !File.ReadAllText(path).Contains(old) && File.ReadAllText(path).Contains("user-tool"), family + " real repair migrates executable and preserves foreign hooks");
            var healthy = ConnectionHealth.Check(family, true); var before = File.ReadAllText(path);
            Storage.Atomic(path, before.Replace("\"hooks\": {", "\"disableAllHooks\": true, \"hooks\": {"));
            var disabled = ConnectionHealth.Check(family, true);
            check(!disabled.Healthy && disabled.Detail.Contains("kapalı") && disabled.Fingerprint != healthy.Fingerprint, family + " health distinguishes global opt-out and changed registration");
            string untouched = File.ReadAllText(path); bool refused = false; try { Integrations.Change(prefs, family, true); } catch (System.IO.InvalidDataException) { refused = true; }
            check(refused && File.ReadAllText(path) == untouched, family + " repair respects explicit user hook opt-out");
            Storage.Atomic(path, before); Integrations.Change(prefs, family, false);
            check(File.ReadAllText(path).Contains("user-tool") && !File.ReadAllText(path).Contains(command), family + " actual uninstall retains foreign trust groups");
            File.Delete(path);
        }
        string codex = Integrations.ConfigPath("codex"), target = Path.Combine(root, "foreign.json"); Storage.Atomic(target, "{\"foreign\":true}"); Directory.CreateDirectory(Path.GetDirectoryName(codex)!); File.CreateSymbolicLink(codex, target);
        bool linkRefused = false; try { Integrations.Change(prefs, "codex", true); } catch (IOException) { linkRefused = true; }
        check(linkRefused && File.ReadAllText(target) == "{\"foreign\":true}", "Hook management preserves a symlink and its user-owned target"); File.Delete(codex);
        string plugin = Integrations.ConfigPath("opencode"), legacy = Path.Combine(Path.GetDirectoryName(plugin)!, LegacyBrand.PluginFile);
        Storage.Atomic(legacy, LegacyBrand.PluginMarker + "\n// old"); Integrations.Change(prefs, "opencode", true);
        check(!File.Exists(legacy) && Integrations.Installed(prefs, "opencode"), "Marked legacy OpenCode plugin migrates with backup"); Integrations.Change(prefs, "opencode", false);
        Storage.Atomic(legacy, "// user-owned"); bool collision = false; try { Integrations.Change(prefs, "opencode", true); } catch (IOException) { collision = true; }
        check(collision && File.ReadAllText(legacy) == "// user-owned" && !File.Exists(plugin), "Foreign legacy OpenCode plugin is never overwritten or deleted"); File.Delete(legacy);
        StartupService.Change(prefs, true); check(StartupService.IsEnabled(prefs) && File.ReadAllText(StartupService.PathName).Contains("--background"), "Opted-in login entry targets background mode in isolated test root");
        string oldStartup=StartupService.Content("/Applications/Old VibeCodingLight.app/Contents/MacOS/VibeCodingLight");
        prefs.StartupRegistration=oldStartup;Storage.Atomic(StartupService.PathName,oldStartup);StartupService.Change(prefs,true);
        check(StartupService.IsEnabled(prefs)&&File.ReadAllText(StartupService.PathName)==StartupService.Content(Storage.Exe),"Owned startup registration follows a moved/installed app without replacing foreign entries");
        string startup = File.ReadAllText(StartupService.PathName); Storage.Atomic(StartupService.PathName, "user-owned-entry"); bool startupRefused = false; try { StartupService.Change(prefs, false); } catch (IOException) { startupRefused = true; }
        check(startupRefused && File.ReadAllText(StartupService.PathName) == "user-owned-entry", "Startup removal preserves a user-modified entry"); Storage.Atomic(StartupService.PathName, startup); StartupService.Change(prefs, false); check(!File.Exists(StartupService.PathName), "Owned startup entry can be removed without touching other entries");
        Integrations.Change(prefs,"codex",true);Integrations.Change(prefs,"claude",true);StartupService.Change(prefs,true);
        Installer.RemoveRegistrations(prefs);
        check(!Integrations.Installed(prefs,"codex")&&!Integrations.Installed(prefs,"claude")&&!StartupService.IsEnabled(prefs),"Application removal cleans only opted-in registrations and its startup entry");
        var self = System.Diagnostics.Process.GetCurrentProcess(); check(ProcessTree.Alive(self.Id, self.StartTime.ToUniversalTime().Ticks) && !ProcessTree.Alive(self.Id, self.StartTime.ToUniversalTime().Ticks - 1), "Process lifetime verifies PID and start time, not PID alone");
        using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sleep", "10") { UseShellExecute = false })!)
        {
            long started = child.StartTime.ToUniversalTime().Ticks; check(ProcessTree.Alive(child.Id, started), "Spawned test owner is live"); child.Kill(); child.WaitForExit(); check(!ProcessTree.Alive(child.Id, started), "Confirmed process exit releases its owner identity");
        }
        string Releases(string version, bool mac = true, bool pre = false) => JsonSerializer.Serialize(new[] { new { tag_name = "v" + version, draft = false, prerelease = pre, assets = new[] { new { name = mac ? "VibeCodingLight-macos-arm64-" + version + ".zip" : "VibeCodingLight-win-x64.zip", browser_download_url = "https://untrusted.invalid/ignored" } } } });
        check(UpdateService.Parse(Releases("0.1.53"), new Version("0.1.52"))?.Version == "0.1.53", "Mac updater offers a newer matching ARM64 package");
        check(UpdateService.Parse(Releases("0.1.52.0"), new Version("0.1.52")) is null, "Equal normalized versions are not offered as updates");
        foreach (string json in new[] { Releases("0.1.99", false), Releases("0.1.99", true, true), "[]" }) { bool unpublished = false; try { UpdateService.Parse(json, new Version("0.1.52")); } catch (InvalidOperationException) { unpublished = true; } check(unpublished, "Missing/staged/Windows-only release is not falsely reported as current Mac version"); }
        using var client = new HttpClient(new Reply(request => { check(request.RequestUri?.AbsoluteUri == UpdateService.ReleasesUrl, "Update lookup stays on the fixed public release endpoint"); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Releases("0.1.53")) }; }));
        check((await UpdateService.Check(client, new Version("0.1.52")))?.Version == "0.1.53", "Mac online update path uses bounded metadata and fixed navigation, never remote commands");
        Storage.Save("settings.json", original);
    }
    internal static async Task HookProcesses(Action<bool, string> check, System.Net.Sockets.Socket listener)
    {
        foreach (string family in Integrations.Families)
        {
            using var timeout = new CancellationTokenSource(5000); var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var listening = Bridge.Listen(listener, text => received.TrySetResult(text), timeout.Token);
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(Storage.Exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }; info.ArgumentList.Add("--hook"); info.ArgumentList.Add(family);
                if (family == "copilot") info.ArgumentList.Add("userPromptSubmitted"); if (family == "antigravity") info.ArgumentList.Add("PreInvocation");
                using var child = System.Diagnostics.Process.Start(info)!;
                string payload = family == "antigravity" ? "{\"conversationId\":\"e2e\",\"invocationId\":\"turn\",\"prompt\":\"SECRET\"}" : "{\"session_id\":\"e2e\",\"hook_event_name\":\"UserPromptSubmit\",\"prompt\":\"SECRET\"}";
                await child.StandardInput.WriteAsync(payload); child.StandardInput.Close();
                string wire = await received.Task.WaitAsync(timeout.Token); await child.WaitForExitAsync(timeout.Token);
                var e = JsonSerializer.Deserialize<HookEvent>(wire); check(e?.Harness == family && !wire.Contains("SECRET"), family + " actual helper process sends sanitized Unix IPC");
                check(child.ExitCode == 0 && await child.StandardError.ReadToEndAsync() == "" && await child.StandardOutput.ReadToEndAsync() == (family == "antigravity" ? "{}" + Environment.NewLine : ""), family + " hook output remains non-blocking and harness-compatible");
            }
            finally { timeout.Cancel(); await listening; }
        }
    }
    private sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }
}
