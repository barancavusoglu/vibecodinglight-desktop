using System.Text.Json;

namespace AiLight.Core;

public enum Activity { Idle, Working, NeedsInput, Unknown }
public enum LightEffect { None, IdleBreathing, WorkingAndIdle }
public record HookEvent(string Id, string Harness, string SessionId, string EventName,
    string TurnId, string ToolId, string ToolName, string Project, string Notification,
    bool StopHookActive, long Timestamp, int OwnerPid = 0, long OwnerStart = 0, string Correlation = "", string Title = "", string Source = "", string MonitorRoot = "",
    bool OneShot = false, string ProducerId = "", long Sequence = 0);

public sealed class Session {
    public string Key { get; set; } = "";
    public string Harness { get; set; } = "";
    public string Project { get; set; } = "";
    public string Title { get; set; } = "";
    public string Turn { get; set; } = "";
    public Activity Activity { get; set; }
    public string Reason { get; set; } = "";
    public long LastEvent { get; set; }
    public int OwnerPid { get; set; }
    public long OwnerStart { get; set; }
    public HashSet<string> Pending { get; set; } = [];
    public Dictionary<string, string> ToolCalls { get; set; } = [];
    public Dictionary<string, string> ToolCorrelations { get; set; } = [];
    public HashSet<string> Children { get; set; } = [];
    public bool MainFinished { get; set; }
    public bool IsOpen { get; set; }
    public bool ResumePending { get; set; }
    public bool AwaitingPrompt { get; set; }
    public long ResumePromptAt { get; set; }
    public bool ResumeReplayObserved { get; set; }
    public bool OneShot { get; set; }
    public bool ExcludedFromLight { get; set; }
    public string ProducerId { get; set; } = "";
    public long Sequence { get; set; }
}

public sealed class StateEngine {
    private readonly Dictionary<string, Session> sessions = [];
    private readonly HashSet<string> seen = [];
    private readonly Queue<string> seenOrder = [];
    private readonly HashSet<string> endedProducers = [];
    private readonly Queue<string> endedProducerOrder = [];
    public IReadOnlyCollection<Session> Sessions => sessions.Values;
    public Func<Session, bool> IsVisible { get; set; } = _ => true;
    public bool Included(Session s) => IsVisible(s) && s.IsOpen && !s.ExcludedFromLight;
    public bool HasIdleSession => sessions.Values.Any(s => Included(s) && s.Activity == Activity.Idle);
    public Activity Aggregate => sessions.Values.Any(s => Included(s) && s.Activity == Activity.NeedsInput) ? Activity.NeedsInput
        : sessions.Values.Any(s => Included(s) && s.Activity == Activity.Working) ? Activity.Working : Activity.Idle;
    public LightEffect Effect => Aggregate == Activity.NeedsInput || !HasIdleSession ? LightEffect.None
        : Aggregate == Activity.Working ? LightEffect.WorkingAndIdle : LightEffect.IdleBreathing;

    public void Apply(HookEvent e) {
        if (new[] { e.Id, e.Harness, e.SessionId, e.EventName, e.TurnId, e.ToolId, e.ToolName,
            e.Project, e.Title, e.Source, e.Correlation, e.ProducerId }.Any(value => value is null)) return;
        if (e.Harness == "opencode" && e.ProducerId.Length > 0) {
            if (endedProducers.Contains(e.ProducerId)) return;
            if (e.EventName == "OpenCodeDisconnected") {
                endedProducers.Add(e.ProducerId); endedProducerOrder.Enqueue(e.ProducerId);
                if (endedProducerOrder.Count > 1024) endedProducers.Remove(endedProducerOrder.Dequeue());
                foreach (var owned in sessions.Values.Where(s => s.Harness == "opencode" && s.ProducerId == e.ProducerId)) OwnerEnded(owned.Key);
                return;
            }
        }
        if (string.IsNullOrWhiteSpace(e.SessionId) || !HarnessCatalog.Families.Contains(e.Harness)) return;
        bool known = HarnessCatalog.IsPreview(e.Harness)
            ? e.EventName is "SessionStart" or "SessionEnd" or "UserPromptSubmit" or "ProviderWorking" or "ProviderIdle" or "ProviderAsked" or "ProviderReplied"
            : e.Harness == "opencode"
            ? e.EventName is "SessionStart" or "SessionEnd" or "Interrupt" or "OpenCodeMetadata" or "OpenCodeWorking" or "OpenCodeIdle" or "OpenCodeError" or "OpenCodeAsked" or "OpenCodeReplied"
            : HookConfig.Events(e.Harness).Contains(e.EventName) || e.EventName is "PostToolUseFailure" or "StopFailure" or "Interrupt";
        if (!known || e.EventName == "Notification" && e.Notification != "permission_prompt") return;
        if (!seen.Add(e.Id)) return;
        seenOrder.Enqueue(e.Id);
        if (seenOrder.Count > 4096) seen.Remove(seenOrder.Dequeue());
        string key = e.Harness + ":" + e.SessionId;
        if (!sessions.TryGetValue(key, out var s)) {
            // Metadata, completions and shutdowns cannot prove a live session.
            if (e.EventName is "ProviderIdle" or "ProviderReplied" or "OpenCodeMetadata" or "OpenCodeIdle" or "OpenCodeReplied" or "SessionEnd" or "Stop" or "StopFailure" or "Interrupt" or "SubagentStop" or "PostToolUse" or "PostToolUseFailure") return;
            if (sessions.Count >= 1000) {
                var oldest = sessions.Values.Where(x => x.Activity == Activity.Idle).MinBy(x => x.LastEvent);
                if (oldest is null) return;
                sessions.Remove(oldest.Key);
            }
            s = new Session { Key = key, Harness = e.Harness };
            sessions[key] = s;
        }
        if (e.Harness == "opencode" && e.ProducerId.Length > 0 && s.ProducerId == e.ProducerId && e.Sequence <= s.Sequence) return;
        if (e.Timestamp < s.LastEvent) return;
        if (e.EventName == "OpenCodeMetadata") {
            if (e.Title.Length > 0) s.Title = CleanTitle(e.Title);
            if (s.ProducerId == e.ProducerId) s.Sequence = e.Sequence;
            return; // A rename must not reopen a closed session or prolong its life.
        }
        bool reattached = e.EventName == "OpenCodeWorking" && e.ProducerId.Length > 0 && e.ProducerId != s.ProducerId;
        if (!s.IsOpen && s.LastEvent > 0 && !reattached && e.EventName is not ("SessionStart" or "UserPromptSubmit" or "ProviderWorking")) return;
        bool newTurn = e.EventName == "UserPromptSubmit";
        if (!newTurn && e.TurnId.Length > 0 && s.Turn.Length > 0 && e.TurnId != s.Turn) return;
        // Codex Desktop replays the last UserPromptSubmit immediately after a
        // resumed conversation is merely opened. It carries the prior turn ID
        // (or no turn ID for older history), so it must not look like fresh work.
        bool resumedReplay = e.Harness == "codex" && newTurn && s.ResumePending
            && (s.Turn.Length == 0 || e.TurnId.Length == 0 || e.TurnId == s.Turn);
        s.LastEvent = e.Timestamp;
        if (e.ProducerId.Length > 0) { s.ProducerId = e.ProducerId; s.Sequence = e.Sequence; }
        s.OneShot |= e.OneShot;
        s.IsOpen = e.EventName != "SessionEnd";
        if (e.Project.Length > 0) s.Project = e.Project;
        if (e.Title.Length > 0) s.Title = CleanTitle(e.Title);
        if (e.OwnerPid > 0) { s.OwnerPid = e.OwnerPid; s.OwnerStart = e.OwnerStart; }
        // A resumed live turn has a human-sized pause between the replayed prompt
        // and its first tool call. Historical replay starts immediately. Use only
        // that first tool event as evidence so a long history replay cannot turn
        // the light green later in the sequence.
        if (s.AwaitingPrompt && s.ResumePending && s.ResumePromptAt > 0 && !s.ResumeReplayObserved
            && e.EventName is "PreToolUse" or "PostToolUse" or "PostToolUseFailure") {
            s.ResumeReplayObserved = true;
            if (e.EventName == "PreToolUse" && e.Timestamp - s.ResumePromptAt >= 2_000) {
                s.ResumePending = false;
                s.AwaitingPrompt = false;
            }
        }
        // Opening or resuming a Codex/Claude conversation can replay old tool
        // events. They are metadata until a fresh prompt or delayed live tool call
        // proves a new turn.
        if (s.AwaitingPrompt && e.EventName is not ("SessionStart" or "UserPromptSubmit" or "SessionEnd" or "Interrupt")) return;
        if (e.TurnId.Length > 0) s.Turn = e.TurnId;
        string token = e.ToolId.Length > 0 ? "id:" + e.ToolId : e.ToolName.Length > 0 ? "name:" + e.ToolName : "permission";
        switch (e.EventName) {
            case "ProviderWorking":
                s.MainFinished = false;
                if (s.Pending.Count == 0) Set(s, Activity.Working, "Çalışıyor");
                break;
            case "ProviderIdle":
                s.MainFinished = true;
                if (s.Pending.Count == 0) Set(s, Activity.Idle, "Tamamlandı");
                break;
            case "ProviderAsked":
                if (e.Harness == "antigravity" && e.ToolName == "ask_question" && e.ToolId.Length > 0) {
                    s.MainFinished = false;
                    s.Pending.Add(token);
                    Set(s, Activity.NeedsInput, "Cevabını bekliyor");
                }
                break;
            case "ProviderReplied":
                if (e.Harness == "antigravity" && e.ToolName == "ask_question" && e.ToolId.Length > 0) {
                    s.Pending.Remove(token);
                    if (s.Pending.Count == 0) Set(s, s.MainFinished ? Activity.Idle : Activity.Working, s.MainFinished ? "Tamamlandı" : "Çalışıyor");
                }
                break;
            case "OpenCodeMetadata": break;
            case "OpenCodeWorking":
                s.MainFinished = false; s.Pending.Remove("opencode:error");
                if (s.Pending.Count == 0) Set(s, Activity.Working, "Çalışıyor");
                break;
            case "OpenCodeIdle":
                s.MainFinished = true;
                if (s.Pending.Count == 0) Set(s, Activity.Idle, "Tamamlandı");
                break;
            case "OpenCodeError":
                s.Pending.Add("opencode:error"); Set(s, Activity.NeedsInput, "Hata · OpenCode'u kontrol et"); break;
            case "OpenCodeAsked":
                s.Pending.Add(token); Set(s, Activity.NeedsInput, e.ToolId.StartsWith("question:") ? "Cevabını bekliyor" : "İzin bekliyor"); break;
            case "OpenCodeReplied":
                s.Pending.Remove(token);
                if (s.Pending.Count == 0) Set(s, s.MainFinished ? Activity.Idle : Activity.Working, s.MainFinished ? "Tamamlandı" : "Çalışıyor");
                break;
            case "SessionStart":
                // Compaction restarts the harness inside an already-running turn.
                // Re-arming the replay guard here would discard its final Stop.
                if (e.Harness is "codex" or "claude" && e.Source.Equals("compact", StringComparison.OrdinalIgnoreCase)
                    && s.Activity is Activity.Working or Activity.NeedsInput && !s.AwaitingPrompt) break;
                s.ResumePending = e.Harness == "codex" && e.Source.Equals("resume", StringComparison.OrdinalIgnoreCase);
                s.AwaitingPrompt = e.Harness is "codex" or "claude";
                s.ResumePromptAt = 0; s.ResumeReplayObserved = false;
                if (s.Activity is Activity.Unknown or Activity.Idle) Set(s, Activity.Idle, "Hazır");
                break;
            case "UserPromptSubmit":
                if (resumedReplay) {
                    s.AwaitingPrompt = true; s.ResumePromptAt = e.Timestamp; s.ResumeReplayObserved = false;
                    Set(s, Activity.Idle, "Hazır"); break;
                }
                s.ResumePending = false; s.AwaitingPrompt = false;
                s.ResumePromptAt = 0; s.ResumeReplayObserved = false;
                s.Pending.Clear(); s.ToolCalls.Clear(); s.ToolCorrelations.Clear(); s.Children.Clear(); s.MainFinished = false;
                Set(s, Activity.Working, "Çalışıyor"); break;
            case "PermissionRequest":
                s.ResumePending = false;
                // Some permission payloads omit the call ID. Bind to an observed
                // call only when unambiguous; otherwise keep a tool-name fallback.
                if (e.Correlation.Length > 0) token = "match:" + e.Correlation;
                else if (e.ToolId.Length == 0 && e.ToolName.Length > 0) {
                    var matches = s.ToolCalls.Where(p => p.Value == e.ToolName).Select(p => p.Key).ToArray();
                    if (matches.Length == 1) token = "id:" + matches[0];
                }
                s.Pending.Add("permit:" + token); Set(s, Activity.NeedsInput, "İzin bekliyor"); break;
            case "Notification":
                if (e.Notification == "permission_prompt") {
                    s.Pending.Add("permit:permission"); Set(s, Activity.NeedsInput, "İzin bekliyor");
                }
                break;
            case "PreToolUse":
                s.ResumePending = false; s.ResumePromptAt = 0; s.ResumeReplayObserved = false;
                if (e.ToolId.Length > 0) s.ToolCalls[e.ToolId] = e.ToolName;
                if (e.ToolId.Length > 0 && e.Correlation.Length > 0) s.ToolCorrelations[e.ToolId] = e.Correlation;
                s.MainFinished = false;
                if (IsQuestion(e.ToolName)) {
                    s.Pending.Add(token); Set(s, Activity.NeedsInput, "Cevabını bekliyor");
                } else if (s.Pending.Count == 0) Set(s, Activity.Working, "Çalışıyor");
                break;
            case "PostToolUse": case "PostToolUseFailure":
                string completedName = e.ToolName;
                string completedId = e.ToolId;
                // A harness may use different IDs across its exec paths. Only
                // reconcile those IDs when the keyed command match is unique.
                if (!s.ToolCalls.ContainsKey(completedId) && e.Correlation.Length > 0) {
                    var matches = s.ToolCorrelations.Where(p => p.Value == e.Correlation).Select(p => p.Key).ToArray();
                    if (matches.Length == 1) completedId = matches[0];
                }
                s.ToolCorrelations.Remove(completedId);
                if (completedId != e.ToolId && s.ToolCalls.Remove(completedId, out var aliasName) && completedName.Length == 0) completedName = aliasName;
                if (e.ToolId.Length > 0 && s.ToolCalls.Remove(e.ToolId, out var trackedName) && completedName.Length == 0) completedName = trackedName;
                // The async question tool returns as soon as its chooser is shown;
                // the answer arrives as the next user prompt, not this completion.
                bool awaitingAsyncAnswer = e.EventName == "PostToolUse" && IsAsyncQuestion(completedName);
                if (e.ToolId.Length > 0) {
                    if (!awaitingAsyncAnswer) s.Pending.Remove(token);
                    s.Pending.Remove("permit:" + token);
                }
                if (completedId.Length > 0) s.Pending.Remove("permit:id:" + completedId);
                if (e.Correlation.Length > 0 && !s.ToolCorrelations.Values.Contains(e.Correlation)) s.Pending.Remove("permit:match:" + e.Correlation);
                // Do not release an ambiguous name-only permission while another
                // observed call of that tool is still outstanding.
                if (completedName.Length > 0 && !s.ToolCalls.Values.Contains(completedName)) {
                    if (!awaitingAsyncAnswer) s.Pending.Remove("name:" + completedName);
                    s.Pending.Remove("permit:name:" + completedName);
                }
                if (s.ToolCalls.Count == 0) s.Pending.Remove("permit:permission");
                if (s.Pending.Count == 0) {
                    bool finished = s.MainFinished && s.Children.Count == 0;
                    Set(s, finished ? Activity.Idle : Activity.Working, finished ? "Tamamlandı" : "Çalışıyor");
                    if (finished && s.OneShot) s.IsOpen = false;
                }
                break;
            case "SubagentStart":
                s.ResumePending = false;
                s.Children.Add(token);
                if (s.Pending.Count == 0) Set(s, Activity.Working, "Alt ajan çalışıyor");
                break;
            case "SubagentStop":
                s.Children.Remove(token);
                if (s.MainFinished && s.Children.Count == 0 && s.Pending.Count == 0) {
                    Set(s, Activity.Idle, "Tamamlandı");
                    if (s.OneShot) s.IsOpen = false;
                }
                break;
            case "Stop":
                s.ResumePending = false;
                s.MainFinished = true;
                if (s.Children.Count == 0) {
                    s.Pending.RemoveWhere(p => p.StartsWith("permit:", StringComparison.Ordinal));
                    s.ToolCalls.Clear(); s.ToolCorrelations.Clear();
                }
                if (s.Pending.Count == 0 && s.Children.Count == 0) {
                    Set(s, Activity.Idle, "Tamamlandı");
                    if (s.OneShot) s.IsOpen = false;
                }
                break;
            case "Interrupt": case "SessionEnd": case "StopFailure":
                s.ResumePending = false; s.AwaitingPrompt = false;
                s.ResumePromptAt = 0; s.ResumeReplayObserved = false;
                s.Pending.Clear(); s.ToolCalls.Clear(); s.ToolCorrelations.Clear(); s.Children.Clear();
                Set(s, Activity.Idle, e.EventName == "StopFailure" ? "Tur hata ile bitti" : "Durduruldu / kapandı");
                break;
        }
    }
    // Codex Desktop may route a PermissionRequest through its local automatic
    // reviewer. That review is separate from the tool completion hook, so a
    // long-running, already-approved command must not keep the light red.
    public bool ResolveAutomaticPermission(string sessionId) {
        if (!sessions.TryGetValue("codex:" + sessionId, out var s)) return false;
        string? permission = s.Pending.FirstOrDefault(p => p.StartsWith("permit:", StringComparison.Ordinal));
        if (permission is null) return false;
        s.Pending.Remove(permission);
        if (s.Pending.Count == 0)
            Set(s, s.MainFinished ? Activity.Idle : Activity.Working, s.MainFinished ? "Tamamlandı" : "Çalışıyor");
        return true;
    }
    private static void Set(Session s, Activity activity, string reason) { s.Activity = activity; s.Reason = reason; }
    public static string CleanTitle(string text) => string.Concat(text.EnumerateRunes()
        .Where(r => !System.Text.Rune.IsControl(r)).Take(240).Select(r => r.ToString()));
    public static bool IsQuestion(string tool) => tool.Equals("AskUserQuestion", StringComparison.OrdinalIgnoreCase)
        || tool.EndsWith("request_user_input", StringComparison.OrdinalIgnoreCase)
        || tool.EndsWith("request_user_input_async", StringComparison.OrdinalIgnoreCase);
    private static bool IsAsyncQuestion(string tool) => tool.EndsWith("request_user_input_async", StringComparison.OrdinalIgnoreCase);
    public void Remove(string key) => sessions.Remove(key);
    public string[] PruneIdleSessions(long now) {
        const long idleLimitMs = 15 * 60 * 1000;
        var expired = sessions.Values.Where(s => s.IsOpen && s.Activity == Activity.Idle
            && s.Pending.Count == 0 && s.Children.Count == 0
            && s.LastEvent > 0 && s.LastEvent <= now - idleLimitMs)
            .Select(s => s.Key).ToArray();
        foreach (var key in expired) sessions.Remove(key);
        return expired;
    }
    public void ToggleLight(string key) {
        if (sessions.TryGetValue(key, out var s)) s.ExcludedFromLight = !s.ExcludedFromLight;
    }
    public void OwnerEnded(string key) {
        if (sessions.TryGetValue(key, out var s)) {
            s.IsOpen = false;
            s.ResumePending = false;
            s.AwaitingPrompt = false;
            s.ResumePromptAt = 0; s.ResumeReplayObserved = false;
            s.Pending.Clear(); s.ToolCalls.Clear(); s.ToolCorrelations.Clear(); s.Children.Clear(); Set(s, Activity.Idle, "Süreç kapandı");
        }
    }
    public void Restore(IEnumerable<Session> previous) {
        foreach (var s in previous.Take(1000)) {
            s.IsOpen = false;
            s.ResumePending = false;
            s.AwaitingPrompt = false;
            s.ResumePromptAt = 0; s.ResumeReplayObserved = false;
            s.Pending.Clear(); s.ToolCalls.Clear(); s.ToolCorrelations.Clear(); s.Children.Clear();
            Set(s, Activity.Unknown, "Yeni olay bekleniyor"); sessions[s.Key] = s;
        }
    }
    public static (int R, int G, int B) Color(Activity state, int brightness) {
        int level = (int)Math.Round(Math.Clamp(brightness, 0, 100) * 255.0 / 100);
        return state == Activity.NeedsInput ? (level, 0, 0) : state == Activity.Working ? (0, level, 0) : (0, 0, 0);
    }
    // A working session plus an idle session is shown as solid yellow.
    public static (int R, int G, int B) MixedColor(int brightness, double seconds) {
        int level = (int)Math.Round(Math.Clamp(brightness, 0, 100) * 255.0 / 100);
        return (level, level, 0);
    }
    public static bool IdleBreathingExpired(bool enabled, int minutes, TimeSpan elapsed) =>
        enabled && elapsed >= TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 240));
}

public sealed class CodexApprovalFeed {
    private readonly Dictionary<string, string> outcomes = [];
    private readonly Dictionary<string, string> sessions = [];
    private readonly HashSet<string> completed = [];
    private string parentSession = "";

    public (string SessionId, string Outcome)? Push(string line) {
        try {
            using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 48 });
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var payload)) return null;
            if (Text(root, "type") == "session_meta") {
                parentSession = Text(payload, "parent_thread_id") is { Length: > 0 } parent ? parent : Text(payload, "session_id");
                return null;
            }
            string type = Text(payload, "type");
            string turn = Text(payload, "turn_id");
            if (type == "token_usage_record") {
                string session = Text(payload, "session_id");
                if (turn.Length > 0 && session.Length > 0) sessions[turn] = session;
            } else if (type == "message" && Text(payload, "phase") == "final_answer") {
                if (turn.Length == 0 && payload.TryGetProperty("internal_chat_message_metadata_passthrough", out var metadata))
                    turn = Text(metadata, "turn_id");
                if (payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                    && content.GetArrayLength() > 0)
                    RememberOutcome(turn, Text(content[0], "text"));
            } else if (type == "task_complete") {
                RememberOutcome(turn, Text(payload, "last_agent_message"));
            }
            if (turn.Length == 0 || completed.Contains(turn)
                || !outcomes.TryGetValue(turn, out var outcome)) return null;
            string sessionId = sessions.GetValueOrDefault(turn, parentSession);
            if (sessionId.Length == 0) return null;
            completed.Add(turn); sessions.Remove(turn); outcomes.Remove(turn);
            if (completed.Count > 4096) completed.Clear();
            return (sessionId, outcome);
        } catch (JsonException) { return null; }
    }

    private void RememberOutcome(string turn, string text) {
        if (turn.Length == 0 || text.Length == 0) return;
        try {
            using var decision = JsonDocument.Parse(text);
            var root = decision.RootElement;
            // These companion fields distinguish the private auto-review result
            // from an arbitrary assistant message that happens to contain JSON.
            if (!root.TryGetProperty("risk_level", out _)
                || !root.TryGetProperty("user_authorization", out _)) return;
            string outcome = Text(root, "outcome");
            if (outcome is "allow" or "deny") outcomes[turn] = outcome;
        } catch (JsonException) { }
    }
    private static string Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
}

public static class EventReader {
    public static HookEvent Read(string harness, string json, int pid = 0, long start = 0, byte[]? correlationKey = null) {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = doc.RootElement;
        string S(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        string cwd = S("cwd").TrimEnd('/', '\\');
        string project = cwd.Split('/', '\\').LastOrDefault() ?? "";
        var toolId = S("tool_use_id");
        if (toolId.Length == 0) toolId = S("agent_id");
        string correlation = "";
        string monitorRoot = "";
        bool oneShot = false;
        string title = harness == "opencode" ? StateEngine.CleanTitle(S("session_title")) : "";
        if (S("hook_event_name") is "SessionStart" or "Stop") {
            if (harness == "codex") oneShot = HarnessMetadata.IsOneShot(S("transcript_path"), S("session_id"));
            if (harness == "claude") title = HarnessMetadata.ClaudeTitle(S("transcript_path"), S("session_id"));
        }
        if (harness == "codex" && S("transcript_path") is { Length: > 0 } transcript) {
            try {
                var directory = new System.IO.FileInfo(transcript).Directory;
                while (directory is not null && !directory.Name.Equals("sessions", StringComparison.OrdinalIgnoreCase)) directory = directory.Parent;
                monitorRoot = directory?.FullName ?? "";
            } catch (Exception e) when (e is ArgumentException or NotSupportedException) { }
        }
        if (harness == "codex" && correlationKey is { Length: 32 }
            && S("hook_event_name") is "PreToolUse" or "PermissionRequest" or "PostToolUse" or "PostToolUseFailure"
            && root.TryGetProperty("tool_input", out var toolInput) && toolInput.ValueKind == JsonValueKind.Object
            && toolInput.TryGetProperty("command", out var command)) {
            // Only a keyed digest leaves this helper, never command text/arguments.
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(S("tool_name") + "\n" + command.GetRawText());
            correlation = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(correlationKey, bytes));
        }
        return new(Guid.NewGuid().ToString("N"), harness, S("session_id"), S("hook_event_name"),
            S("turn_id"), toolId, S("tool_name"), project, S("notification_type"),
            root.TryGetProperty("stop_hook_active", out var flag) && flag.ValueKind == JsonValueKind.True,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), pid, start, correlation,
            title, S("source"), monitorRoot, oneShot,
            harness == "opencode" ? S("producer_id") : "",
            harness == "opencode" && root.TryGetProperty("sequence", out var seq) && seq.TryGetInt64(out long sequence) ? sequence : 0);
    }
}
