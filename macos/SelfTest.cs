using System.Text.Json;
using AiLight.Core;
namespace AiLight.Mac;
public static class SelfTest {
    public static int Run() {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/tmp" : Path.GetTempPath(), "ai-light-mac-" + Guid.NewGuid().ToString("N"));
        try {
            Environment.SetEnvironmentVariable("AI_LIGHT_MAC_TEST_ROOT", root);
            Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(root, "codex"));
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(root, "claude"));
            Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", Path.Combine(root, "opencode"));
            Environment.SetEnvironmentVariable("COPILOT_HOME", Path.Combine(root, "copilot"));
            var prefs = new Preferences { Brightness = 37, Theme = "light" }; Storage.Save("settings.json", prefs);
            Check(Storage.Load().Brightness == 30 && Storage.Load().Theme == "light", "macOS preference migration matches Windows brightness steps");
            foreach (string theme in DesktopThemes.All) {
                prefs.Theme = theme; Storage.Save("settings.json", prefs);
                Check(Storage.Load().Theme == theme, "Theme persists: " + theme);
            }
            Check(ColorWheel.Hsv(0,1)==(255,0,0) && ColorWheel.Hsv(120,1)==(0,255,0) && ColorWheel.Hsv(240,1)==(0,0,255), "Color wheel primary hues");
            Check(ColorWheel.Hsv(0,0)==(255,255,255) && ColorWheel.Hsv(60,1)==(255,255,0), "Color wheel center and mixed hue");
            Check(Runtime.ScaleTestColor((255,128,0),50)==(128,64,0) && Runtime.ScaleTestColor((255,255,255),0)==(0,0,0), "Color test respects brightness and off");
            var previewTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int hueSelections = 0;
            var lightPreview = new ManualLightPreview(() => previewTime, () => { hueSelections++; return 240; });
            Check(lightPreview.StartBrightness(Activity.Idle) && lightPreview.IsBrightness && lightPreview.Color == (0,0,255), "Brightness click previews visible color even without a session");
            var low = Runtime.ScaleTestColor(lightPreview.Color, 15);
            var high = Runtime.ScaleTestColor(lightPreview.Color, 100);
            Check(low == (0,0,38) && high == (0,0,255), "Brightness presets immediately scale the same preview color");
            previewTime = previewTime.AddSeconds(2);
            lightPreview.StartBrightness(Activity.Working);
            Check(hueSelections == 1 && lightPreview.Color == (0,0,255), "Repeated brightness clicks retain preview hue");
            previewTime = previewTime.AddSeconds(2);
            Check(lightPreview.Update(Activity.Working), "Repeated click extends the preview from the last selection");
            previewTime = previewTime.AddSeconds(1);
            Check(!lightPreview.Update(Activity.Working) && !lightPreview.IsBrightness, "Brightness preview expires exactly three seconds after last click");
            Check(!lightPreview.StartBrightness(Activity.NeedsInput) && !lightPreview.IsActive, "Brightness cannot override a pending question or permission");
            lightPreview.StartBrightness(Activity.Working);
            Check(!lightPreview.Update(Activity.NeedsInput), "New pending input immediately interrupts brightness preview");
            lightPreview.StartColor(255,0,0);
            Check(lightPreview.Update(Activity.NeedsInput) && !lightPreview.IsBrightness, "Explicit color-wheel test keeps its separate manual behavior");
            lightPreview.StartBrightness(Activity.Idle);
            Check(lightPreview.IsBrightness && lightPreview.Color == (0,0,255) && hueSelections == 3, "Brightness selection after a color test starts its own preview");
            lightPreview.Finish();
            Check(!lightPreview.IsActive && !lightPreview.IsBrightness, "Scheduled preview expiry restores normal output");
            MacParityTests.Run(Check,root,prefs).GetAwaiter().GetResult();
            Check(Storage.Key().SequenceEqual(Storage.Key()), "Stable installation correlation key");
            Check(Integrations.Command("/Users/O'Brien/VibeCodingLight", "codex") == "'/Users/O'\"'\"'Brien/VibeCodingLight' --hook codex", "POSIX apostrophe and space quoting");
            foreach (string family in Integrations.Families) {
                string path = Integrations.ConfigPath(family);
                string original = "{\"theme\":\"user\",\"hooks\":{\"PreToolUse\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"user-tool\"}]}]}}";
                if (family == "copilot") original = "{\"theme\":\"user-tool\"}";
                if (family != "opencode") Storage.Atomic(path, original);
                Integrations.Change(prefs, family, true); Check(Integrations.Installed(prefs, family), family + " install");
                string first = File.ReadAllText(path); Integrations.Change(prefs, family, true);
                Check(first == File.ReadAllText(path), family + " repair idempotent");
                Integrations.Change(prefs, family, false);
                Check(family == "opencode" ? !File.Exists(path) : File.ReadAllText(path).Contains("user-tool") && !File.ReadAllText(path).Contains("--hook"), family + " remove preserves user hooks");
            }
            string pluginPath = Integrations.ConfigPath("opencode"); Storage.Atomic(pluginPath,"user-owned");
            bool rejected = false; try { Integrations.Change(prefs,"opencode",true); } catch (IOException) { rejected = true; }
            Check(rejected && File.ReadAllText(pluginPath) == "user-owned", "Plugin collision preserved");
            var state = new StateEngine(); long time = 0;
            HookEvent E(string id, string kind, string tool = "") => new(Guid.NewGuid().ToString(), "opencode", id, kind, "", tool, "", "Demo", "", false, ++time);
            state.Apply(E("one", "OpenCodeWorking")); state.Apply(E("two","SessionStart"));
            Check(state.Effect == LightEffect.WorkingAndIdle, "Shared mixed session effect");
            state.Apply(E("two", "OpenCodeAsked", "question:q")); Check(state.Aggregate == Activity.NeedsInput && state.Effect == LightEffect.None, "Shared red priority");
            state.Apply(E("two", "OpenCodeReplied", "question:q")); state.Apply(E("one", "OpenCodeIdle")); state.Apply(E("two", "OpenCodeIdle"));
            Check(state.Effect == LightEffect.IdleBreathing, "Shared idle breathing");
            Check(StateEngine.MixedColor(100, 5) == (255,255,0) && StateEngine.MixedColor(0, 5) == (0,0,0), "Shared yellow and brightness");
            var parsed = EventReader.Read("claude", "{\"session_id\":\"s\",\"hook_event_name\":\"Stop\",\"prompt\":\"SECRET\"}");
            Check(!JsonSerializer.Serialize(parsed).Contains("SECRET"), "Shared privacy filtering");
            using var listener = Bridge.Bind(); using var stop = new CancellationTokenSource(3000);
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = Bridge.Listen(listener, text => received.TrySetResult(text), stop.Token);
            Check(Bridge.Send("TEST").GetAwaiter().GetResult(), "Unix socket delivery");
            Check(received.Task.WaitAsync(stop.Token).GetAwaiter().GetResult() == "TEST", "Unix socket framing");
            stop.Cancel(); task.GetAwaiter().GetResult();
            using var hookTimeout = new CancellationTokenSource(5000);
            var hookReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var hookTask = Bridge.Listen(listener, text => hookReceived.TrySetResult(text), hookTimeout.Token);
            var info = new System.Diagnostics.ProcessStartInfo(Storage.Exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            info.ArgumentList.Add("--hook"); info.ArgumentList.Add("opencode");
            using var child = System.Diagnostics.Process.Start(info)!;
            child.StandardInput.Write("{\"session_id\":\"s\",\"hook_event_name\":\"OpenCodeWorking\",\"session_title\":\"Title\",\"prompt\":\"SECRET\"}"); child.StandardInput.Close();
            string hookWire = hookReceived.Task.WaitAsync(hookTimeout.Token).GetAwaiter().GetResult();
            Check(child.WaitForExit(3000) && child.ExitCode == 0 && child.StandardOutput.ReadToEnd() == "" && child.StandardError.ReadToEnd() == "", "Real helper silent success");
            Check(JsonSerializer.Deserialize<HookEvent>(hookWire)?.Harness == "opencode" && !hookWire.Contains("SECRET"), "Real helper sanitized IPC");
            hookTimeout.Cancel(); hookTask.GetAwaiter().GetResult();
            var ag = new System.Diagnostics.ProcessStartInfo(Storage.Exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            ag.ArgumentList.Add("--hook"); ag.ArgumentList.Add("antigravity"); ag.ArgumentList.Add("PreInvocation");
            using var agChild = System.Diagnostics.Process.Start(ag)!;
            agChild.StandardInput.Write("{}"); agChild.StandardInput.Close();
            Check(agChild.WaitForExit(3000) && agChild.ExitCode == 0 && agChild.StandardOutput.ReadToEnd() == "{}" + Environment.NewLine && agChild.StandardError.ReadToEnd() == "", "Antigravity returns neutral JSON on invalid event");
            var question = ProviderHooks.Read("antigravity", "PreToolUse", "{\"conversationId\":\"q\",\"stepIdx\":3,\"toolCall\":{\"name\":\"ask_question\",\"args\":{\"question\":\"PRIVATE\"}}}");
            Check(question?.EventName == "ProviderAsked" && question.ToolId == "step:3" && !JsonSerializer.Serialize(question).Contains("PRIVATE"), "Antigravity question metadata only");
            MacParityTests.HookProcesses(Check,listener).GetAwaiter().GetResult();
            Console.WriteLine($"{checks} macOS service checks passed. Test data: {root}"); return 0;
        } catch (Exception e) { Console.Error.WriteLine("FAIL " + e); return 1; }
    }
}
