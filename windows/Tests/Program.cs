using AiLight.Core;
using System.Text.Json.Nodes;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS " + name); }
await UpdateChecks.Run(Check);
long timestamp = 100;
HookEvent E(string session, string name, string turn = "t1", string tool = "Bash", string toolId = "a", string harness = "codex") =>
    new(Guid.NewGuid().ToString(), harness, session, name, turn, toolId, tool, "project", "", false, timestamp++);
var state = new StateEngine();
for (int i = 0; i < 10; i++) state.Apply(E(i.ToString(), "UserPromptSubmit"));
Check(state.Aggregate == Activity.Working && state.Sessions.Count == 10, "10 active sessions = green");
state.Apply(E("1", "PermissionRequest"));
Check(state.Aggregate == Activity.NeedsInput, "One pending overrides nine working");
state.Apply(E("2", "PermissionRequest")); state.Apply(E("1", "PostToolUse"));
Check(state.Aggregate == Activity.NeedsInput, "One remaining pending keeps red");
state.Apply(E("2", "PostToolUse")); Check(state.Aggregate == Activity.Working, "Responses restore green");
for (int i = 0; i < 9; i++) state.Apply(E(i.ToString(), "Stop"));
Check(state.Aggregate == Activity.Working, "Last working keeps green");
state.Apply(E("9", "Stop")); Check(state.Aggregate == Activity.Idle, "All complete = off");
state.Apply(E("0", "UserPromptSubmit", "t2")); state.Apply(E("0", "Stop", "t1"));
Check(state.Aggregate == Activity.Working, "Stale turn cannot stop new work");
var wait = E("0", "PermissionRequest", "t2"); state.Apply(wait); state.Apply(E("0", "PostToolUse", "t2")); state.Apply(wait);
Check(state.Aggregate == Activity.Working, "Duplicate event does not re-open permission");
state.Apply(E("0", "PreToolUse", "t2", "request_user_input")); state.Apply(E("0", "Stop", "t2"));
Check(state.Aggregate == Activity.NeedsInput, "Structured question keeps wait across stop");
var asyncQuestion = new StateEngine();
asyncQuestion.Apply(E("async-answer", "UserPromptSubmit"));
asyncQuestion.Apply(E("async-answer", "PreToolUse", tool: "functions.request_user_input_async", toolId: "chooser"));
asyncQuestion.Apply(E("async-answer", "PostToolUse", tool: "functions.request_user_input_async", toolId: "chooser"));
asyncQuestion.Apply(E("async-answer", "Stop"));
Check(asyncQuestion.Aggregate == Activity.NeedsInput, "Async question remains red after its tool returns and turn stops");
asyncQuestion.Apply(E("async-answer", "UserPromptSubmit", "reply"));
Check(asyncQuestion.Aggregate == Activity.Working, "User choice releases async question wait");
var failedAsyncQuestion = new StateEngine();
failedAsyncQuestion.Apply(E("failed-chooser", "UserPromptSubmit"));
failedAsyncQuestion.Apply(E("failed-chooser", "PreToolUse", tool: "functions.request_user_input_async", toolId: "chooser"));
failedAsyncQuestion.Apply(E("failed-chooser", "PostToolUseFailure", tool: "functions.request_user_input_async", toolId: "chooser"));
Check(failedAsyncQuestion.Aggregate == Activity.Working, "Failed async chooser does not leave a red light");
state.Apply(E("0", "Interrupt", "t2")); Check(state.Aggregate == Activity.Idle, "Interrupt clears wait");
state.Apply(E("0", "UserPromptSubmit", "t3")); state.Apply(E("0", "SubagentStart", "t3", "Agent", "child1")); state.Apply(E("0", "Stop", "t3"));
Check(state.Aggregate == Activity.Working, "Child outlives parent turn");
state.Apply(E("0", "SubagentStop", "t3", "Agent", "child1")); Check(state.Aggregate == Activity.Idle, "Child completion releases light");
state.Apply(E("0", "UserPromptSubmit", "t4")); state.OwnerEnded("codex:0"); Check(state.Aggregate == Activity.Idle, "Confirmed owner exit clears stale state");
var restored = new StateEngine(); restored.Restore(state.Sessions); Check(restored.Sessions.All(s => s.Activity == Activity.Unknown) && restored.Aggregate == Activity.Idle, "Restart does not resurrect stale green/red");
var resumed = new StateEngine();
resumed.Restore(new[] { new Session { Key = "codex:resume", Harness = "codex", Turn = "old", MainFinished = true } });
resumed.Apply(E("resume", "SessionStart", "old") with { Source = "resume" });
resumed.Apply(E("resume", "UserPromptSubmit", "old"));
Check(resumed.Aggregate == Activity.Idle && resumed.HasIdleSession, "Opening resumed Codex history ignores replayed prior prompt");
resumed.Apply(E("resume", "UserPromptSubmit", "new"));
Check(resumed.Aggregate == Activity.Working, "Fresh turn in resumed Codex session still starts work");
var unknownResume = new StateEngine();
unknownResume.Apply(E("unknown-resume", "SessionStart", "") with { Source = "resume" });
unknownResume.Apply(E("unknown-resume", "UserPromptSubmit", "historic"));
Check(unknownResume.Aggregate == Activity.Idle, "First prompt replay in previously unseen resumed history stays idle");
unknownResume.Apply(E("unknown-resume", "PreToolUse", "historic"));
Check(unknownResume.Aggregate == Activity.Idle, "Replayed tool activity cannot start a resumed conversation");
unknownResume.Apply(E("unknown-resume", "PreToolUse", "historic") with { Timestamp = 10_000 });
Check(unknownResume.Aggregate == Activity.Idle, "Long replay sequence cannot become live after first replayed tool");
unknownResume.Apply(E("unknown-resume", "UserPromptSubmit", "fresh") with { Timestamp = 11_000 });
Check(unknownResume.Aggregate == Activity.Working, "Fresh prompt after resume starts work");
var activeResume = new StateEngine();
activeResume.Apply(E("active-resume", "SessionStart", "") with { Source = "resume", Timestamp = 20_000 });
activeResume.Apply(E("active-resume", "UserPromptSubmit", "active") with { Timestamp = 20_500 });
activeResume.Apply(E("active-resume", "PreToolUse", "active") with { Timestamp = 23_500 });
Check(activeResume.Aggregate == Activity.Working, "Delayed first tool confirms active resumed Codex turn");
foreach (var harness in new[] { "codex", "claude" }) {
    var compacted = new StateEngine();
    compacted.Apply(E("compacted", "UserPromptSubmit", harness: harness));
    compacted.Apply(E("compacted", "SessionStart", harness: harness) with { Source = "compact" });
    compacted.Apply(E("compacted", "PreToolUse", toolId: "after-compact", harness: harness));
    compacted.Apply(E("compacted", "Stop", harness: harness));
    Check(compacted.Aggregate == Activity.Idle && compacted.HasIdleSession,
        $"{harness} compaction during a live turn cannot hide its final Stop");
}
var opened = new StateEngine();
opened.Apply(E("opened", "SessionStart") with { Source = "startup" });
opened.Apply(E("opened", "PreToolUse", "historic"));
Check(opened.Aggregate == Activity.Idle && opened.HasIdleSession, "Opening a conversation cannot turn green before a prompt");
opened.Apply(E("opened", "UserPromptSubmit", "fresh"));
Check(opened.Aggregate == Activity.Working, "New conversation starts on a real prompt");
Check(StateEngine.Color(Activity.NeedsInput, 100) == (255,0,0), "Full brightness red");
Check(StateEngine.Color(Activity.Working, 50) == (0,128,0), "Half brightness green");
Check(StateEngine.Color(Activity.NeedsInput, 0) == (0,0,0), "Zero brightness overrides output");
var input = """{"session_id":"s","hook_event_name":"PreToolUse","tool_name":"AskUserQuestion","cwd":"C:\\Users\\Someone\\Proje adı","prompt":"secret prompt","tool_input":{"private":"secret"}}""";
var parsed = EventReader.Read("claude", input);
Check(parsed.Project == "Proje adı" && StateEngine.IsQuestion(parsed.ToolName), "Privacy-minimal event normalization");
Check(!System.Text.Json.JsonSerializer.Serialize(parsed).Contains("secret"), "No prompt/tool arguments forwarded");
string expectedSessionsRoot = Path.Combine(Path.GetTempPath(), "vibecodinglight-codex-fixture", "sessions");
string transcriptJson = System.Text.Json.JsonSerializer.Serialize(new {
    session_id = "s", hook_event_name = "SessionStart",
    transcript_path = Path.Combine(expectedSessionsRoot, "2026", "09", "16", "rollout.jsonl"),
    cwd = Path.GetTempPath()
});
Check(EventReader.Read("codex", transcriptJson).MonitorRoot == Path.GetFullPath(expectedSessionsRoot), "Codex hook supplies the exact sessions root without transcript content");
string cmd = "\"C:\\Users\\Test User\\AI Light.exe\" --hook codex";
string original = """{"theme":"dark","hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"user-tool"}]}]}}""";
string once = HookConfig.Edit(original, cmd, [], "codex", true);
string twice = HookConfig.Edit(once, cmd, [cmd], "codex", true);
Check(once == twice, "Idempotent install");
var removed = JsonNode.Parse(HookConfig.Edit(twice, cmd, [cmd], "codex", false))!;
Check(removed["theme"]!.GetValue<string>() == "dark" && removed["hooks"]!["PreToolUse"]!.AsArray().Count == 1, "Uninstall preserves user config");
Check(removed["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>() == "user-tool", "Uninstall preserves unrelated hook");
bool rejected = false;
try { HookConfig.Edit("{\"hooks\":[]}", cmd, [], "codex", true); } catch { rejected = true; }
Check(rejected, "Malformed hook schema rejected");
var stopState = new StateEngine(); stopState.Apply(E("stop-active", "UserPromptSubmit"));
stopState.Apply(E("stop-active", "Stop") with { StopHookActive = true });
Check(stopState.Aggregate == Activity.Idle, "Final stop after another stop hook still turns off");
string windowsCommand = HookConfig.WindowsCommand("C:\\Users\\O'Brien $test & name\\AI Light.exe", "codex");
Check(HookConfig.WindowsCommand("C:\\Users\\TestUser\\AI Light.exe", "codex") == "powershell.exe -NoProfile -NonInteractive -Command \"& 'C:\\Users\\TestUser\\AI Light.exe' --hook codex\"", "Ordinary Codex path uses readable command");
string decoded = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(windowsCommand.Split(' ')[^1]));
Check(decoded == "& 'C:\\Users\\O''Brien $test & name\\AI Light.exe' --hook codex", "Codex command safely quotes Windows path");
string migrated = HookConfig.Edit(once, windowsCommand, [cmd], "codex", true);
Check(!migrated.Contains(System.Text.Json.JsonSerializer.Serialize(cmd)) && migrated.Contains("user-tool"), "Migration removes old owned command only");
Check(migrated == HookConfig.Edit(migrated, windowsCommand, [windowsCommand], "codex", true), "Migrated command repair is idempotent");
Check(HookConfig.WindowsCommand("C:\\Test\\AI Light.exe", "claude") == "\"C:\\Test\\AI Light.exe\" --hook claude", "Claude command unchanged");
var idle = new StateEngine();
Check(!idle.HasIdleSession, "No session does not breathe");
idle.Apply(E("idle", "SessionStart"));
Check(idle.HasIdleSession && idle.Aggregate == Activity.Idle, "Open idle session can breathe");
idle.Apply(E("idle", "UserPromptSubmit"));
Check(!idle.HasIdleSession && idle.Aggregate == Activity.Working, "Working overrides breathing");
idle.Apply(E("idle", "Stop"));
Check(idle.HasIdleSession, "Finished turn keeps open idle session");
idle.Apply(E("other", "PermissionRequest"));
Check(idle.Aggregate == Activity.NeedsInput, "Red overrides another idle session");
idle.Apply(E("idle", "SessionEnd"));
Check(!idle.HasIdleSession, "Closed session history does not breathe");
idle.Apply(E("idle", "SessionStart")); idle.OwnerEnded("codex:idle");
Check(!idle.HasIdleSession, "Dead owner does not breathe");
idle.Apply(E("idle", "SessionStart")); var reloaded = new StateEngine(); reloaded.Restore(idle.Sessions);
Check(!reloaded.HasIdleSession, "Restored unknown sessions do not breathe");
foreach (var harness in new[] { "codex", "claude", "opencode" }) {
    const long startedAt = 1_700_000_000_000;
    const long idleLimitMs = 15 * 60 * 1000;
    var stale = new StateEngine();
    if (harness == "opencode") {
        stale.Apply(E("finished", "OpenCodeWorking", harness: harness) with { Timestamp = startedAt });
        stale.Apply(E("finished", "OpenCodeIdle", harness: harness) with { Timestamp = startedAt + 10_000 });
    } else {
        stale.Apply(E("finished", "UserPromptSubmit", harness: harness) with { Timestamp = startedAt });
        stale.Apply(E("finished", "Stop", harness: harness) with { Timestamp = startedAt + 10_000 });
    }
    stale.Apply(E("active", harness == "opencode" ? "OpenCodeWorking" : "UserPromptSubmit", harness: harness)
        with { Timestamp = startedAt });
    stale.Apply(E("blocked", harness == "opencode" ? "OpenCodeAsked" : "PermissionRequest", harness: harness)
        with { Timestamp = startedAt });
    Check(stale.PruneIdleSessions(startedAt + 10_000 + idleLimitMs - 1).Length == 0,
        $"{harness} idle session remains before 15 minutes");
    Check(stale.PruneIdleSessions(startedAt + 10_000 + idleLimitMs).SequenceEqual(new[] { harness + ":finished" })
        && stale.Sessions.Count == 2 && stale.Aggregate == Activity.NeedsInput,
        $"{harness} idle session expires at 15 minutes without dropping work or approval");
    stale.Apply(E("finished", harness == "opencode" ? "OpenCodeWorking" : "UserPromptSubmit", harness: harness)
        with { Timestamp = startedAt + 10_000 + idleLimitMs + 1 });
    Check(stale.Sessions.Count == 3 && stale.Sessions.Single(s => s.Key == harness + ":finished").Activity == Activity.Working,
        $"{harness} new activity can recreate an expired session");
}
Check(!StateEngine.IdleBreathingExpired(false, 10, TimeSpan.FromHours(1)), "Disabled idle timeout never turns light off");
Check(!StateEngine.IdleBreathingExpired(true, 10, TimeSpan.FromMinutes(9.99)), "Idle breathing remains before timeout");
Check(StateEngine.IdleBreathingExpired(true, 10, TimeSpan.FromMinutes(10)), "Idle breathing turns off at configured timeout");
var permissionState = new StateEngine();
permissionState.Apply(E("p", "PermissionRequest", toolId: ""));
permissionState.Apply(E("p", "PostToolUse", toolId: "completed-id"));
Check(permissionState.Aggregate == Activity.Working, "Name-only permission cleared by matching ID-bearing completion");
permissionState.Apply(E("p", "PreToolUse", toolId: "one"));
permissionState.Apply(E("p", "PermissionRequest", toolId: ""));
permissionState.Apply(E("p", "PostToolUse", toolId: ""));
Check(permissionState.Aggregate == Activity.NeedsInput, "ID-less completion cannot clear tracked in-flight permission");
permissionState.Apply(E("p", "PreToolUse", toolId: "two"));
permissionState.Apply(E("p", "PostToolUse", toolId: "two"));
Check(permissionState.Aggregate == Activity.NeedsInput, "Unrelated same-name completion preserves bound permission");
permissionState.Apply(E("p", "PostToolUseFailure", toolId: "one"));
Check(permissionState.Aggregate == Activity.Working, "Matching failed completion clears permission");
var delayed = new StateEngine();
delayed.Apply(E("delayed", "UserPromptSubmit")); delayed.Apply(E("delayed", "Stop")); delayed.Apply(E("delayed", "PostToolUse"));
Check(delayed.Aggregate == Activity.Idle, "Late tool completion cannot resurrect a finished turn");
permissionState.Apply(E("p", "PreToolUse", toolId: "one"));
permissionState.Apply(E("p", "PreToolUse", toolId: "two"));
permissionState.Apply(E("p", "PermissionRequest", toolId: ""));
permissionState.Apply(E("p", "PostToolUse", toolId: "one"));
Check(permissionState.Aggregate == Activity.NeedsInput, "Ambiguous parallel permission stays red until both finish");
permissionState.Apply(E("p", "PostToolUse", toolId: "two"));
Check(permissionState.Aggregate == Activity.Working, "Last same-name completion clears ambiguous fallback");
permissionState.Apply(E("q", "PreToolUse", tool: "request_user_input", toolId: "question"));
permissionState.Apply(E("p", "PermissionRequest", toolId: ""));
permissionState.Apply(E("p", "PostToolUse", toolId: "done"));
Check(permissionState.Aggregate == Activity.NeedsInput, "Clearing permission preserves another session's question");
var correlated = new StateEngine();
correlated.Apply(E("c", "PreToolUse", toolId: "long-running") with { Correlation = "long" });
correlated.Apply(E("c", "PreToolUse", toolId: "permitted") with { Correlation = "short" });
correlated.Apply(E("c", "PermissionRequest", toolId: "") with { Correlation = "short" });
correlated.Apply(E("c", "PostToolUse", toolId: "different-id") with { Correlation = "short" });
Check(correlated.Aggregate == Activity.Working && correlated.Sessions.Single().ToolCalls.ContainsKey("long-running"), "Command correlation releases permission without waiting for unrelated Bash");
correlated.Apply(E("c", "PermissionRequest", toolId: "") with { Correlation = "long" });
correlated.Apply(E("c", "Stop"));
Check(correlated.Aggregate == Activity.Idle && correlated.Sessions.Single().Pending.Count == 0, "Completed turn clears stale permission");
var automatic = new StateEngine();
automatic.Apply(E("auto", "UserPromptSubmit"));
automatic.Apply(E("auto", "PermissionRequest", toolId: "auto-permission"));
Check(automatic.Aggregate == Activity.NeedsInput, "Permission stays red while automatic review is unresolved");
Check(automatic.ResolveAutomaticPermission("auto") && automatic.Aggregate == Activity.Working, "Automatic approval immediately restores working state");
Check(!automatic.ResolveAutomaticPermission("auto"), "Duplicate automatic approval is ignored");
var permissionGate = new PermissionLightGate();
var gateState = new StateEngine();
gateState.Apply(E("gate", "UserPromptSubmit"));
var gateStart = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
var gateEvent = E("gate", "PermissionRequest", toolId: "reviewed-command");
gateState.Apply(gateEvent);
Check(permissionGate.Observe(gateEvent, false, gateState, gateStart)
    && permissionGate.Hold(gateState, OutputMode.Green, gateStart.AddSeconds(3.5)),
    "Codex automatic review keeps the previous LED color for four seconds");
Check(LightPresentation.CheckingPermission(gateState.Sessions.Single(), true)
    && LightPresentation.SessionActivity(gateState.Sessions.Single(), true, OutputMode.Green) == Activity.Working
    && LightPresentation.SessionActivity(gateState.Sessions.Single(), true, OutputMode.Blue) == Activity.Idle,
    "Window session follows the held green or blue light without showing red");
Check(!permissionGate.Hold(gateState, OutputMode.Green, gateStart.AddSeconds(4.1)),
    "Unresolved real Codex permission turns red after four seconds");
Check(!LightPresentation.CheckingPermission(gateState.Sessions.Single(), false)
    && LightPresentation.SessionActivity(gateState.Sessions.Single(), false, OutputMode.Red) == Activity.NeedsInput,
    "Window session turns red with the light after unresolved grace");
permissionGate.ReviewStarted("gate", gateStart.AddSeconds(1));
Check(permissionGate.Hold(gateState, OutputMode.Green, gateStart.AddSeconds(7.4))
    && !permissionGate.Hold(gateState, OutputMode.Green, gateStart.AddSeconds(13.1)),
    "Active Codex auto-review extends hold with a safety limit");
Check(!permissionGate.Hold(gateState, OutputMode.Test, gateStart.AddSeconds(1)),
    "A manual color test is not held by the permission gate");
permissionGate.ReviewFinished("gate");
gateState.ResolveAutomaticPermission("gate");
Check(!permissionGate.Hold(gateState, OutputMode.Green, gateStart.AddSeconds(2.2)),
    "Automatic approval prevents the transient red light");
var questionState = new StateEngine();
questionState.Apply(E("question-gate", "UserPromptSubmit"));
questionState.Apply(E("question-gate", "PermissionRequest"));
permissionGate.Observe(E("question-gate", "PermissionRequest"), false, questionState, gateStart);
questionState.Apply(E("question-gate", "PreToolUse", tool: "request_user_input", toolId: "question"));
Check(!permissionGate.Hold(questionState, OutputMode.Green, gateStart.AddSeconds(1)),
    "A real user question interrupts permission grace immediately");
var otherPermission = new StateEngine();
otherPermission.Apply(E("other-gate", "UserPromptSubmit") with { Harness = "claude" });
otherPermission.Apply(E("other-gate", "PermissionRequest") with { Harness = "claude" });
Check(!permissionGate.Hold(otherPermission, OutputMode.Green, gateStart.AddSeconds(1)),
    "Claude permission remains immediately visible");
Check(!LightPresentation.CheckingPermission(otherPermission.Sessions.Single(), true)
    && LightPresentation.SessionActivity(otherPermission.Sessions.Single(), true, OutputMode.Green) == Activity.NeedsInput,
    "Other harness permissions are never hidden by Codex review presentation");
automatic.Apply(E("auto", "PreToolUse", tool: "request_user_input", toolId: "question"));
automatic.Apply(E("auto", "PermissionRequest", toolId: "second-permission"));
automatic.ResolveAutomaticPermission("auto");
Check(automatic.Aggregate == Activity.NeedsInput, "Automatic approval never clears a real question");
var approvalFeed = new CodexApprovalFeed();
string reviewMeta = """{"type":"session_meta","payload":{"session_id":"parent-from-meta","thread_source":"guardian_review"}}""";
string reviewMessage = """{"timestamp":"x","type":"response_item","payload":{"type":"message","phase":"final_answer","content":[{"type":"output_text","text":"{\"risk_level\":\"low\",\"user_authorization\":\"high\",\"outcome\":\"allow\",\"rationale\":\"private\"}"}],"internal_chat_message_metadata_passthrough":{"turn_id":"review-turn"}}}""";
string reviewUsage = """{"timestamp":"x","type":"token_usage_record","payload":{"type":"token_usage_record","turn_id":"review-turn","session_id":"parent-session"}}""";
Check(approvalFeed.Push(reviewMessage) is null, "Auto-review result waits for parent session mapping");
var autoResult = approvalFeed.Push(reviewUsage);
Check(autoResult is { SessionId: "parent-session", Outcome: "allow" }, "Auto-review allow maps back to the parent Codex session");
Check(approvalFeed.Push(reviewUsage) is null, "Auto-review resolution is emitted once");
var metadataFeed = new CodexApprovalFeed(); metadataFeed.Push(reviewMeta);
var metadataResult = metadataFeed.Push(reviewMessage);
Check(metadataResult is { SessionId: "parent-from-meta", Outcome: "allow" }, "Auto-review maps from session metadata when token usage is absent");
var parentFeed = new CodexApprovalFeed();
parentFeed.Push("""{"type":"session_meta","payload":{"session_id":"review-session","parent_thread_id":"actual-parent"}}""");
Check(parentFeed.Push(reviewMessage) is { SessionId: "actual-parent", Outcome: "allow" },
    "Review metadata maps to the parent Codex session");
string ordinaryJson = """{"type":"response_item","payload":{"type":"message","phase":"final_answer","content":[{"text":"{\"outcome\":\"allow\"}"}],"internal_chat_message_metadata_passthrough":{"turn_id":"ordinary"}}}""";
Check(approvalFeed.Push(ordinaryJson) is null, "Ordinary assistant JSON cannot impersonate an auto-review decision");
correlated.Apply(E("c", "UserPromptSubmit", turn: "t2"));
correlated.Apply(E("c", "PreToolUse", turn: "t2", tool: "request_user_input", toolId: "q"));
correlated.Apply(E("c", "PermissionRequest", turn: "t2", toolId: "p"));
correlated.Apply(E("c", "Stop", turn: "t2"));
Check(correlated.Aggregate == Activity.NeedsInput, "Final stop preserves question while clearing permission");
byte[] testKey = Enumerable.Range(0,32).Select(i => (byte)i).ToArray();
string permissionJson = """{"session_id":"s","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"private command","description":"allow?"}}""";
string postJson = """{"session_id":"s","hook_event_name":"PostToolUse","tool_use_id":"new-id","tool_name":"Bash","tool_input":{"command":"private command"}}""";
var permissionEvent = EventReader.Read("codex", permissionJson, correlationKey: testKey);
var postEvent = EventReader.Read("codex", postJson, correlationKey: testKey);
Check(permissionEvent.Correlation.Length == 64 && permissionEvent.Correlation == postEvent.Correlation, "Command digest ignores description and differing IDs");
Check(!System.Text.Json.JsonSerializer.Serialize(permissionEvent).Contains("private command"), "Command text never forwarded");
Check(EventReader.Read("codex", postJson, correlationKey: new byte[32]).Correlation != postEvent.Correlation, "Correlation digest is installation-keyed");
var names = TitleIndex.Read(new[] {
    """{"id":"s","thread_name":"Yeni başlık","updated_at":"2026-09-07T12:00:00Z"}""",
    "broken json", """{"id":"other","thread_name":"Not in scope"}""",
    """{"id":"s","thread_name":"Eski başlık","updated_at":"2026-09-06T12:00:00Z"}"""
}, new HashSet<string> { "s" });
Check(names.Count == 1 && names["s"] == "Yeni başlık", "Title index matches exact ID and latest name, tolerates malformed line");
var catalog = TitleIndex.ReadCatalog(new[] {
    """{"id":"visible","thread_name":"Kullanıcı sohbeti"}""",
    """{"id":"untitled"}""", "broken json"
}, new HashSet<string> { "visible" });
Check(catalog.KnownIds.SetEquals(new[] { "visible", "untitled" }) && catalog.Titles["visible"] == "Kullanıcı sohbeti",
    "Codex catalog recognizes user-facing IDs even before a title exists");
var visibleOnly = new StateEngine { IsVisible = s => s.Harness != "codex" || s.Key == "codex:visible" };
visibleOnly.Apply(E("visible", "UserPromptSubmit"));
visibleOnly.Apply(E("hidden", "PermissionRequest"));
Check(visibleOnly.Aggregate == Activity.Working, "Hidden Codex session cannot turn the light red");
visibleOnly.Apply(E("visible", "Stop"));
Check(visibleOnly.Aggregate == Activity.Idle && visibleOnly.Effect == LightEffect.IdleBreathing,
    "Hidden Codex session cannot alter visible idle effect");
var mixed = new StateEngine();
Check(mixed.Effect == LightEffect.None, "No sessions = no effect");
mixed.Apply(E("worker", "UserPromptSubmit"));
mixed.Apply(E("finished", "UserPromptSubmit", harness: "claude"));
Check(mixed.Effect == LightEffect.None, "Only working sessions = solid green");
mixed.Apply(E("finished", "Stop", harness: "claude"));
Check(mixed.Effect == LightEffect.WorkingAndIdle, "Working plus completed across harnesses = yellow mode");
Check(StateEngine.MixedColor(100, 0) == (255,255,0) && StateEngine.MixedColor(100, 99) == (255,255,0), "Mixed state stays solid yellow");
Check(StateEngine.MixedColor(50, 0) == (128,128,0) && StateEngine.MixedColor(50, 5) == (128,128,0), "Yellow respects brightness");
Check(StateEngine.MixedColor(0, 0) == (0,0,0) && StateEngine.MixedColor(0, 5) == (0,0,0), "Zero brightness disables yellow mode");
mixed.Apply(E("worker", "PermissionRequest"));
Check(mixed.Aggregate == Activity.NeedsInput && mixed.Effect == LightEffect.None, "Permission immediately overrides mixed effect with red");
mixed.Apply(E("worker", "PostToolUse"));
Check(mixed.Effect == LightEffect.WorkingAndIdle, "Permission completion restores mixed mode");
mixed.Apply(E("worker", "Stop"));
Check(mixed.Effect == LightEffect.IdleBreathing, "Last working session completes = blue breathing");
mixed.Apply(E("worker", "UserPromptSubmit", turn: "t2"));
mixed.Remove("claude:finished");
Check(mixed.Effect == LightEffect.None && mixed.Aggregate == Activity.Working, "Removing last idle session returns solid green");
mixed.Apply(E("closed", "SessionStart")); mixed.Apply(E("closed", "SessionEnd"));
Check(mixed.Effect == LightEffect.None, "Closed history does not trigger mixed effect");
mixed.Apply(E("ready", "SessionStart"));
Check(mixed.Effect == LightEffect.WorkingAndIdle, "Open ready session also accepts a prompt and triggers mixed effect");
mixed.OwnerEnded("codex:ready");
Check(mixed.Effect == LightEffect.None, "Exited idle owner does not trigger mixed effect");
var unknownMixed = new StateEngine(); unknownMixed.Restore(mixed.Sessions);
unknownMixed.Apply(E("new-worker", "UserPromptSubmit"));
Check(unknownMixed.Effect == LightEffect.None, "Restored unknown history does not trigger mixed effect");
var oc = new StateEngine();
HookEvent O(string name, string id = "") => E("oc", name, turn: "", toolId: id, harness: "opencode");
oc.Apply(O("SessionStart") with { Title = "OpenCode başlığı" });
Check(oc.HasIdleSession && oc.Sessions.Single().Title == "OpenCode başlığı", "OpenCode starts idle with title");
oc.Apply(O("OpenCodeWorking"));
Check(oc.Aggregate == Activity.Working, "OpenCode busy = working");
oc.Apply(O("OpenCodeAsked", "permission:p")); oc.Apply(O("OpenCodeAsked", "question:q"));
oc.Apply(O("OpenCodeWorking")); oc.Apply(O("OpenCodeReplied", "permission:p"));
Check(oc.Aggregate == Activity.NeedsInput, "OpenCode busy and permission reply preserve outstanding question");
oc.Apply(O("OpenCodeReplied", "question:q"));
Check(oc.Aggregate == Activity.Working, "OpenCode last reply restores working");
oc.Apply(O("OpenCodeIdle"));
Check(oc.Effect == LightEffect.IdleBreathing, "OpenCode idle breathes");
oc.Apply(O("OpenCodeAsked", "question:q2")); oc.Apply(O("OpenCodeIdle"));
Check(oc.Aggregate == Activity.NeedsInput, "OpenCode idle cannot clear unanswered question");
oc.Apply(O("OpenCodeReplied", "question:q2"));
Check(oc.Aggregate == Activity.Idle, "Reply after idle returns idle instead of stuck green");
oc.Apply(O("OpenCodeError")); oc.Apply(O("OpenCodeIdle"));
Check(oc.Aggregate == Activity.NeedsInput, "OpenCode error remains blocker across idle");
oc.Apply(O("OpenCodeWorking"));
Check(oc.Aggregate == Activity.Working, "New OpenCode work clears error blocker");
oc.Apply(E("codex-other", "PermissionRequest"));
Check(oc.Aggregate == Activity.NeedsInput, "Other harness wait overrides OpenCode work");
oc.Apply(O("SessionEnd"));
Check(!oc.Sessions.Single(s => s.Harness == "opencode").IsOpen, "OpenCode deletion closes session");
HookRegression.Run(Check);
var filtered = new StateEngine();
filtered.Apply(E("filtered", "UserPromptSubmit"));
filtered.ToggleLight("codex:filtered");
Check(filtered.Aggregate == Activity.Idle && filtered.Effect == LightEffect.None, "Excluded work does not light up");
filtered.Apply(E("filtered", "PermissionRequest"));
Check(filtered.Aggregate == Activity.Idle && filtered.Sessions.Single().Activity == Activity.NeedsInput, "Excluded permission tracked but not red");
filtered.ToggleLight("codex:filtered");
Check(filtered.Aggregate == Activity.NeedsInput, "Include restores latest permission state");
var animationTarget = (R: 0, G: 128, B: 0);
Check(ConnectionAnimation.Render(animationTarget, 50, 0) == (0,0,0), "USB animation starts dark");
var earlyRainbow = ConnectionAnimation.Render(animationTarget, 50, 0.35);
var middleRainbow = ConnectionAnimation.Render(animationTarget, 50, 1.0);
Check(earlyRainbow.R > 0 && earlyRainbow.G > 0 && earlyRainbow.B == 0
    && middleRainbow.B > 0 && middleRainbow != earlyRainbow, "USB animation sweeps rainbow hues");
Check(ConnectionAnimation.Render(animationTarget, 50, 1.99).G > 0
    && ConnectionAnimation.Render(animationTarget, 50, 2.0) == animationTarget,
    "USB animation settles into current status at two seconds");
Check(ConnectionAnimation.Render((0,0,0), 15, 0.9) is var dim && dim.R <= 39 && dim.G <= 39 && dim.B <= 39,
    "USB animation respects selected brightness");
for (int level = 0; level < SettingSteps.BrightnessLevels.Length; level++) Check(SettingSteps.BrightnessLevel(SettingSteps.BrightnessPercent(level)) == level, $"Brightness step {level} roundtrip");
Check(SettingSteps.BrightnessLevel(65) == 3 && SettingSteps.BrightnessLevel(37) == 1, "Legacy brightness snaps to nearest step");
Check(SettingSteps.BrightnessPercent(-1) == 15 && SettingSteps.BrightnessPercent(11) == 100, "Brightness step bounds");
Check(SettingSteps.BrightnessPercent(SettingSteps.BrightnessLevel(0)) == 15 && SettingSteps.BrightnessLevels.All(v => v > 0), "Zero brightness migrates to lowest lit level");
Check(SettingSteps.IdleMinutes.All(m => SettingSteps.NearestIdleMinutes(m) == m), "All duration presets roundtrip");
Check(SettingSteps.NearestIdleMinutes(45) == 30 && SettingSteps.NearestIdleMinutes(int.MaxValue) == 240, "Duration migration and bounds");
ProviderRegression.Run(Check);
await TransportRegression.Run(Check);
await FirmwareEffectRegression.Run(Check);
Console.WriteLine($"{checks} checks passed.");
