using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AiLight.Core;
namespace AiLight.App;
public static class SmokeTest {
    public static async Task<int> Run(string report) {
        try {
            if (UpdateService.Parse("{\"version\":\"0.1.47\"}", new Version(0,1,46,0)) is null)
                throw new Exception("New release notification missing");
            if (UpdateService.Parse("{\"version\":\"0.1.47\"}", new Version(0,1,47,0)) is not null)
                throw new Exception("Current release offered again");
            string testRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, ".test-data", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("AI_LIGHT_DATA_DIR", Path.Combine(testRoot, "data"));
            // Never attach the smoke listener to the user's running tray app.
            Environment.SetEnvironmentVariable("AI_LIGHT_TEST_PIPE", Guid.NewGuid().ToString("N"));
            await VerifyShell("cmd");
            await VerifyShell("powershell");
            await VerifyProviders(testRoot);
            using var cancel = new CancellationTokenSource(5000);
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = Bridge.Listen(text => received.TrySetResult(text), cancel.Token);
            var info = new ProcessStartInfo(Storage.Exe, "--hook codex") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
            using var helper = Process.Start(info)!;
            await helper.StandardInput.WriteAsync("{\"session_id\":\"smoke\",\"hook_event_name\":\"UserPromptSubmit\",\"turn_id\":\"t\",\"cwd\":\"C:/Demo\",\"prompt\":\"MUST_NOT_FORWARD\"}");
            helper.StandardInput.Close();
            string wire = await received.Task.WaitAsync(cancel.Token);
            await helper.WaitForExitAsync(cancel.Token);
            var e = JsonSerializer.Deserialize<HookEvent>(wire)!;
            var state = new StateEngine(); state.Apply(e);
            if (state.Aggregate != AiLight.Core.Activity.Working || wire.Contains("MUST_NOT_FORWARD")) throw new Exception("Hook pipe/state/privacy failed");
            cancel.Cancel(); await server;
            Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(testRoot, "codex"));
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(testRoot, "claude"));
            Storage.Atomic(Path.Combine(testRoot, "codex", "session_index.jsonl"), "{\"id\":\"title-test\",\"thread_name\":\"Sohbet başlığı\"}\n");
            var titleSession = new Session { Key = "codex:title-test", Harness = "codex", Project = "Folder" };
            var titleReader = new SessionTitles(); titleReader.Initialize();
            if (!titleReader.IsVisible(titleSession)
                || titleReader.IsVisible(new Session { Key = "codex:internal-review", Harness = "codex" }))
                throw new Exception("Codex user-facing session filter failed");
            titleReader.Refresh(new[] { titleSession });
            for (int i = 0; i < 30 && titleReader.Get(titleSession) != "Sohbet başlığı"; i++) await Task.Delay(50);
            if (titleReader.Get(titleSession) != "Sohbet başlığı") throw new Exception("Async title lookup failed");
            if (!titleReader.Get(new Session { Key = "claude:123456789", Harness = "claude", Project = "Folder" }).Contains("23456789")) throw new Exception("Title fallback failed");
            var preferences = new Preferences();
            foreach (var family in new[] { "codex", "claude" }) {
                string path = Integrations.ConfigPath(family);
                Storage.Atomic(path, "{\"user_setting\":42,\"hooks\":{\"PreToolUse\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"user-owned\"}]}]}}");
                Integrations.Change(preferences, family, true);
                string first = File.ReadAllText(path);
                Integrations.Change(preferences, family, true);
                if (first != File.ReadAllText(path) || !Integrations.IsInstalled(preferences, family)) throw new Exception("Real install/repair not idempotent");
                if (!ConnectionHealth.Check(family, true).Healthy) throw new Exception("Healthy config reported broken");
                var incomplete = System.Text.Json.Nodes.JsonNode.Parse(first)!;
                incomplete["hooks"]!.AsObject().Remove("PostToolUse");
                Storage.Atomic(path, incomplete.ToJsonString());
                if (Integrations.IsInstalled(preferences, family)) throw new Exception("Partial hook install reported healthy");
                var health = ConnectionHealth.Check(family, true);
                if (health.Healthy || !health.Detail.Contains("PostToolUse")) throw new Exception("Health check must identify missing event");
                Integrations.Change(preferences, family, true);
                if (!Integrations.IsInstalled(preferences, family)) throw new Exception("Missing hook repair failed");
                Integrations.Change(preferences, family, false);
                string after = File.ReadAllText(path);
                if (after.Contains("--hook") || !after.Contains("user-owned") || !after.Contains("42")) throw new Exception("Uninstall damaged user config");
            }
            Storage.Save("settings.json", new Preferences { Brightness = 37, IdleAutoOffEnabled = true, IdleAutoOffMinutes = 45, AutoCheckUpdates = false });
            if (Storage.Load().AutoCheckUpdates) throw new Exception("Update opt-out not persisted");
            Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", Path.Combine(testRoot, "opencode"));
            var ocPreferences = Storage.Load();
            OpenCodeIntegration.Change(ocPreferences, true);
            if (!OpenCodeIntegration.Installed()) throw new Exception("OpenCode install failed");
            string ocFirst = File.ReadAllText(OpenCodeIntegration.PluginPath);
            Storage.Atomic(OpenCodeIntegration.LegacyPluginPath, ocFirst.Replace("// VibeCodingLight managed plugin v1", AiLight.Core.LegacyBrand.PluginMarker));
            OpenCodeIntegration.Change(ocPreferences, true);
            if (File.Exists(OpenCodeIntegration.LegacyPluginPath) || !OpenCodeIntegration.Installed()) throw new Exception("Legacy plugin migration failed");
            OpenCodeIntegration.Change(ocPreferences, true);
            if (ocFirst != File.ReadAllText(OpenCodeIntegration.PluginPath)) throw new Exception("OpenCode repair not idempotent");
            string unrelated = Path.Combine(Path.GetDirectoryName(OpenCodeIntegration.PluginPath)!, "user-plugin.js");
            File.WriteAllText(unrelated, "// user-owned");
            OpenCodeIntegration.Change(ocPreferences, false);
            if (File.Exists(OpenCodeIntegration.PluginPath) || File.ReadAllText(unrelated) != "// user-owned") throw new Exception("OpenCode uninstall damaged files");
            Storage.Atomic(OpenCodeIntegration.LegacyPluginPath, "// user-owned legacy collision");
            bool legacyCollision = false;
            try { OpenCodeIntegration.Change(ocPreferences, true); } catch (IOException) { legacyCollision = true; }
            if (!legacyCollision || File.ReadAllText(OpenCodeIntegration.LegacyPluginPath) != "// user-owned legacy collision" || File.Exists(OpenCodeIntegration.PluginPath)) throw new Exception("Legacy plugin collision not preserved");
            File.Delete(OpenCodeIntegration.LegacyPluginPath); // This test's disposable fixture only.
            File.WriteAllText(OpenCodeIntegration.PluginPath, "// user-owned collision");
            bool protectedCollision = false;
            try { OpenCodeIntegration.Change(ocPreferences, true); } catch (IOException) { protectedCollision = true; }
            if (!protectedCollision || File.ReadAllText(OpenCodeIntegration.PluginPath) != "// user-owned collision") throw new Exception("OpenCode collision not preserved");
            if (Storage.Load().Brightness != 30) throw new Exception("Brightness persistence failed");
            if (!Storage.Load().IdleAutoOffEnabled || Storage.Load().IdleAutoOffMinutes != 30) throw new Exception("Idle auto-off persistence failed");
            if (Storage.Load().Theme != "sage") throw new Exception("Default theme failed");
            foreach (var theme in Themes.All.Concat(new[] { "ocean", "lavender" })) {
                Storage.Save("settings.json", new Preferences { Brightness = 37, Theme = theme });
                if (Storage.Load().Theme != Themes.Normalize(theme) || Storage.Load().Brightness != 30) throw new Exception("Theme persistence failed");
            }
            Storage.Save("settings.json", new Preferences { Theme = "invalid" });
            if (Storage.Load().Theme != "sage") throw new Exception("Invalid theme fallback failed");
            string approvalRoot = Path.Combine(testRoot, "approval-sessions");
            Directory.CreateDirectory(approvalRoot);
            string approvalRollout = Path.Combine(approvalRoot, "review.jsonl");
            File.WriteAllText(approvalRollout, "{\"type\":\"session_meta\",\"payload\":{\"thread_source\":\"guardian_review\",\"session_id\":\"review-session\",\"parent_thread_id\":\"parent-session\"}}\n");
            using (var approvalCancel = new CancellationTokenSource(5000)) {
                var approvalResult = new TaskCompletionSource<(string Session, string Outcome)>(TaskCreationOptions.RunContinuationsAsynchronously);
                var reviewStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var approvalWatcher = new CodexApprovalWatcher(approvalRoot);
                var approvalTask = approvalWatcher.Run((session, outcome) => approvalResult.TrySetResult((session, outcome)),
                    approvalCancel.Token, started: session => reviewStarted.TrySetResult(session));
                await Task.Delay(700);
                string decision = JsonSerializer.Serialize(new { timestamp = "x", type = "response_item", payload = new {
                    type = "message", phase = "final_answer", content = new[] { new { type = "output_text", text = "{\"risk_level\":\"low\",\"user_authorization\":\"high\",\"outcome\":\"allow\"}" } },
                    internal_chat_message_metadata_passthrough = new { turn_id = "review-turn" }
                }});
                string usage = JsonSerializer.Serialize(new { timestamp = "x", type = "token_usage_record", payload = new {
                    type = "token_usage_record", turn_id = "review-turn", session_id = "parent-session"
                }});
                File.AppendAllText(approvalRollout, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n" + decision + "\n" + usage + "\n");
                if (await reviewStarted.Task.WaitAsync(approvalCancel.Token) != "parent-session") throw new Exception("Codex auto-review start not mapped to parent");
                var resolved = await approvalResult.Task.WaitAsync(approvalCancel.Token);
                if (resolved != ("parent-session", "allow")) throw new Exception("Codex auto-review watcher mapping failed");
                approvalCancel.Cancel(); await approvalTask;
            }
            var diagnostics = new Diagnostics();
            var diagnosticEngine = new StateEngine();
            var beforeDiagnostic = Diagnostics.Capture(diagnosticEngine, "codex:diag");
            diagnosticEngine.Apply(new HookEvent("diag1", "codex", "diag", "PermissionRequest", "", "request", "Bash", "PRIVATE_PROJECT", "", false, 1));
            var redDiagnostic = Diagnostics.Capture(diagnosticEngine, "codex:diag");
            diagnostics.Record("PermissionRequest", "codex", "PRIVATE_SESSION", beforeDiagnostic, redDiagnostic, 40, false, true, "PRIVATE_REQUEST", "PRIVATE_COMMAND_DIGEST");
            diagnosticEngine.Apply(new HookEvent("diag2", "codex", "diag", "PostToolUse", "", "request", "Bash", "PRIVATE_PROJECT", "", false, 2));
            diagnostics.Record("PostToolUse", "codex", "PRIVATE_SESSION", redDiagnostic, Diagnostics.Capture(diagnosticEngine, "codex:diag"), 40, false, true, "PRIVATE_REQUEST", "PRIVATE_COMMAND_DIGEST");
            var diagnosticPair = diagnostics.Snapshot();
            if (diagnosticPair[^2].After.Light != "Kırmızı" || diagnosticPair[^1].After.Light != "Yeşil" || diagnosticPair[^1].After.Pending != 0) throw new Exception("Diagnostic transitions failed");
            if (diagnosticPair[^1].SessionTag != diagnosticPair[^2].SessionTag || diagnosticPair[^1].CommandTag != diagnosticPair[^2].CommandTag) throw new Exception("Diagnostic correlation failed");
            for (int i = 0; i < 510; i++) diagnostics.Record("PRIVATE_UNKNOWN_EVENT", "PRIVATE_FAMILY", "PRIVATE_SESSION", beforeDiagnostic, redDiagnostic, 40, false, true);
            await diagnostics.Complete();
            if (diagnostics.Snapshot().Length != Diagnostics.Capacity || (Storage.Read<List<DiagnosticEntry>>(Diagnostics.FileName)?.Count ?? 0) != Diagnostics.Capacity) throw new Exception("Diagnostic retention/persistence failed");
            if (File.ReadAllText(Path.Combine(Storage.Root, Diagnostics.FileName)).Contains("PRIVATE")) throw new Exception("Diagnostic privacy failed");
            var reloadedDiagnostics = new Diagnostics();
            if (reloadedDiagnostics.Snapshot().Length != Diagnostics.Capacity) throw new Exception("Diagnostic reload failed");
            reloadedDiagnostics.Clear(); await reloadedDiagnostics.Complete();
            if (Storage.Read<List<DiagnosticEntry>>(Diagnostics.FileName)?.Count != 0) throw new Exception("Diagnostic clear failed");
            File.WriteAllText(report, "PASS: generated Codex command delivers stdin through both cmd.exe and PowerShell; zero stdout/stderr; isolated pipe; green/red/off transitions.\nPASS: GUI-subsystem hook helper received redirected stdin; current-user named pipe delivered sanitized event; state became Working; no prompt forwarded.\nPASS: isolated Codex/Claude config install, repair, uninstall preserve user-owned settings/hooks.\nPASS: Codex auto-review rollout watcher resolves the parent session without retaining content.\nPASS: brightness save/reload.\n");
            return 0;
        } catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); return 1; }
    }
    private static async Task VerifyShell(string shell) {
        var state = new StateEngine();
        foreach (var (eventName, expected) in new[] {
            ("UserPromptSubmit", AiLight.Core.Activity.Working),
            ("PermissionRequest", AiLight.Core.Activity.NeedsInput),
            ("PostToolUse", AiLight.Core.Activity.Working),
            ("SessionEnd", AiLight.Core.Activity.Idle) }) {
            using var cancel = new CancellationTokenSource(8000);
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = Bridge.Listen(text => received.TrySetResult(text), cancel.Token);
            var command = HookConfig.WindowsCommand(Storage.Exe, "codex");
            var info = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (shell == "cmd") {
                info.FileName = "cmd.exe";
                info.Arguments = "/d /s /c \"" + command + "\"";
            } else {
                info.FileName = "powershell.exe";
                info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive");
                info.ArgumentList.Add("-EncodedCommand");
                info.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command)));
            }
            using var child = Process.Start(info)!;
            try {
                await child.StandardInput.WriteAsync(JsonSerializer.Serialize(new {
                    session_id = "shell-smoke", hook_event_name = eventName, turn_id = "t", cwd = "C:/Demo", prompt = "MUST_NOT_FORWARD",
                    tool_name = "Bash", tool_use_id = eventName == "PostToolUse" ? "call1" : "",
                    tool_input = new { command = "PRIVATE_COMMAND_MUST_NOT_FORWARD", description = eventName }
                }));
                child.StandardInput.Close();
                var stdout = child.StandardOutput.ReadToEndAsync();
                var stderr = child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync(cancel.Token);
                if (child.ExitCode != 0 || (await stdout).Length != 0 || (await stderr).Length != 0)
                    throw new Exception(shell + ": hook must exit zero without output");
                string wire = await received.Task.WaitAsync(cancel.Token);
                state.Apply(JsonSerializer.Deserialize<HookEvent>(wire)!);
                if (wire.Contains("MUST_NOT_FORWARD") || state.Aggregate != expected)
                    throw new Exception(shell + ": state/privacy failed for " + eventName);
            } finally {
                if (!child.HasExited) child.Kill(true);
                cancel.Cancel(); await server;
            }
        }
    }
    private static async Task VerifyProviders(string testRoot) {
        string wrapperPath = Path.Combine(testRoot, "vibecodinglight-hook.cmd");
        Storage.Atomic(wrapperPath, ProviderHooks.AntigravityWrapper(Storage.Exe));
        foreach (var family in new[] { "copilot", "antigravity" }) {
            var state = new StateEngine();
            foreach (var name in ProviderHooks.Events(family)) {
                using var cancel = new CancellationTokenSource(8000);
                var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var server = Bridge.Listen(text => received.TrySetResult(text), cancel.Token);
                string config = ProviderHooks.Edit("{}", ProviderHooks.Command(Storage.Exe, family, true, wrapperPath), [], family, true, true);
                using var document = JsonDocument.Parse(config);
                var entry = document.RootElement.GetProperty(family == "copilot" ? "hooks" : "vibecodinglight").GetProperty(name)[0];
                var action = family == "antigravity" && (name is "PreToolUse" or "PostToolUse") ? entry.GetProperty("hooks")[0] : entry;
                string command = action.GetProperty(family == "copilot" ? "powershell" : "command").GetString()!;
                var info = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                if (family == "copilot") {
                    info.FileName = "powershell.exe";
                    info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-EncodedCommand");
                    info.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command)));
                } else { info.FileName = "cmd.exe"; info.Arguments = "/d /s /c \"" + command + "\""; }
                using var child = Process.Start(info)!;
                try {
                    await child.StandardInput.WriteAsync(JsonSerializer.Serialize(new { sessionId = "provider-smoke", conversationId = "provider-smoke", fullyIdle = true,
                        cwd = "C:/demo", prompt = "MUST_NOT_FORWARD", stepIdx = 4,
                        toolCall = new { name = "ask_question", args = new { question = "MUST_NOT_FORWARD" } } }));
                    child.StandardInput.Close();
                    var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
                    await child.WaitForExitAsync(cancel.Token);
                    string output = await stdout;
                    string expectedOutput = family == "antigravity"
                        ? (name == "PreToolUse" ? "{\"decision\":\"allow\"}" : "{}") + Environment.NewLine : "";
                    if (child.ExitCode != 0 || output != expectedOutput || (await stderr).Length > 0)
                        throw new Exception(family + " hook output contract failed");
                    string wire = await received.Task.WaitAsync(cancel.Token);
                    var e = JsonSerializer.Deserialize<HookEvent>(wire)!; state.Apply(e);
                    var expected = name is "PreInvocation" or "userPromptSubmitted" or "PostToolUse" ? AiLight.Core.Activity.Working
                        : name == "PreToolUse" ? AiLight.Core.Activity.NeedsInput : AiLight.Core.Activity.Idle;
                    if (wire.Contains("MUST_NOT_FORWARD") || e.Harness != family || state.Aggregate != expected) throw new Exception(family + " packaged transport/state failed");
                } finally { if (!child.HasExited) child.Kill(true); cancel.Cancel(); await server; }
            }
        }
    }
}
