using System.Text.Json;
using System.Text.Json.Nodes;
using AiLight.Core;

static class HookRegression {
    public static void Run(Action<bool, string> check) {
        const string old = "old-ai-light", current = "new-ai-light";
        const string config = """{"theme":"user","hooks":{"Stop":[{"hooks":[{"type":"command","command":"old-ai-light"}]},{"matcher":"user","hooks":[{"type":"command","command":"foreign","timeout":77}]}]}}""";
        string upgraded = HookConfig.Edit(config, current, [old], "codex", true);
        var stop = JsonNode.Parse(upgraded)!["hooks"]!["Stop"]!.AsArray();
        check(stop.Count == 2 && stop[1]!["matcher"]!.GetValue<string>() == "user" && stop[1]!["hooks"]![0]!["timeout"]!.GetValue<int>() == 77,
            "Upgrade preserves foreign group index and all attributes");
        check(stop[0]!["hooks"]![0]!["command"]!.GetValue<string>() == current, "Owned hook upgraded in place");
        string removed = HookConfig.Edit(upgraded, current, [old, current], "codex", false);
        stop = JsonNode.Parse(removed)!["hooks"]!["Stop"]!.AsArray();
        check(stop.Count == 2 && stop[0]!["hooks"]!.AsArray().Count == 0 && stop[1]!["hooks"]![0]!["command"]!.GetValue<string>() == "foreign",
            "Uninstall leaves inert slot before foreign trusted hook");
        check(HookConfig.Edit(removed, current, [current], "codex", true) == upgraded, "Reinstall reuses the same trust slot");
        check(HookConfig.Edit(upgraded, current, [], "codex", true) == upgraded, "Exact current command recovered after lost ownership preferences");
        check(HookConfig.IsInstalled(upgraded, current, "codex"), "All lifecycle hooks constitute healthy installation");
        var duplicate = JsonNode.Parse(upgraded)!;
        duplicate["hooks"]!["Stop"]!.AsArray().Add(duplicate["hooks"]!["Stop"]![0]!.DeepClone());
        check(!HookConfig.IsInstalled(duplicate.ToJsonString(), current, "codex"), "Duplicate hook wiring is detected");
        check(HookConfig.Edit(duplicate.ToJsonString(), current, [current], "codex", true) == upgraded, "Duplicate owned group removed without shifting foreign hook");
        var broken = JsonNode.Parse(upgraded)!;
        broken["hooks"]!.AsObject().Remove("PostToolUse");
        check(!HookConfig.IsInstalled(broken.ToJsonString(), current, "codex"), "Single missing completion hook is detected");
        check(HookConfig.IsInstalled(HookConfig.Edit(broken.ToJsonString(), current, [current], "codex", true), current, "codex"), "Missing completion hook repaired");
        broken = JsonNode.Parse(upgraded)!;
        broken["hooks"]!["PreToolUse"]![0]!["matcher"] = "OnlyOneTool";
        check(!HookConfig.IsInstalled(broken.ToJsonString(), current, "codex"), "Narrow matcher cannot masquerade as complete coverage");
        check(HookConfig.IsInstalled(HookConfig.Edit(broken.ToJsonString(), current, [current], "codex", true), current, "codex"), "Narrow managed matcher repaired");
        const string mixed = """{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"old-ai-light"},{"type":"command","command":"foreign"}]}]}}""";
        var mixedUpgrade = JsonNode.Parse(HookConfig.Edit(mixed, current, [old], "codex", true))!;
        check(mixedUpgrade["hooks"]!["Stop"]![0]!["hooks"]![1]!["command"]!.GetValue<string>() == "foreign", "Mixed group upgrade preserves foreign action index");
        bool refused = false;
        try { HookConfig.Edit(mixed, current, [old], "codex", false); } catch (InvalidDataException) { refused = true; }
        check(refused, "Unsafe mixed-group uninstall refused without changing configuration");
        foreach (string family in new[] { "codex", "claude" }) {
            string disabled = "{\"disableAllHooks\":true}";
            refused = false;
            try { HookConfig.Edit(disabled, current, [], family, true); } catch (InvalidDataException) { refused = true; }
            check(refused && !HookConfig.IsInstalled(disabled, current, family), family + " global hook opt-out respected");
            string installed = HookConfig.Edit(config, current, [old], family, true);
            check(HookConfig.IsInstalled(installed, current, family), family + " complete coverage after upgrade");
        }
        long timestamp = 10000;
        HookEvent E(string name, string sid = "s", string harness = "opencode", string producer = "", long seq = 0) =>
            new(Guid.NewGuid().ToString(), harness, sid, name, "", "", "", "Demo", "", false, timestamp++, ProducerId: producer, Sequence: seq);
        var engine = new StateEngine();
        foreach (string name in new[] { "future.event", "OpenCodeMetadata", "OpenCodeIdle", "SessionEnd", "Stop", "PostToolUse", "SubagentStop" }) engine.Apply(E(name));
        engine.Apply(E("Notification", harness: "claude") with { Notification = "idle_prompt" });
        check(engine.Sessions.Count == 0, "Unknown, informational, title-only and terminal events do not create ghosts");
        engine.Apply(E("OpenCodeWorking", producer: "a", seq: 1));
        engine.Apply(E("OpenCodeIdle", producer: "a", seq: 3));
        engine.Apply(E("OpenCodeWorking", producer: "a", seq: 2));
        check(engine.Aggregate == Activity.Idle, "Out-of-order OpenCode progress cannot undo idle");
        engine.Apply(E("SessionEnd", producer: "a", seq: 4));
        engine.Apply(E("OpenCodeMetadata", producer: "a", seq: 5) with { Title = "Renamed" });
        check(!engine.HasIdleSession && engine.Sessions.Single().Title == "Renamed", "Rename after close changes title without reopening");
        engine.Apply(E("OpenCodeWorking", producer: "a", seq: 6));
        check(!engine.Sessions.Single().IsOpen, "Same producer's trailing busy cannot reopen deleted session");
        engine.Apply(E("OpenCodeWorking", "a", producer: "a", seq: 6));
        engine.Apply(E("OpenCodeWorking", "b", producer: "b", seq: 1));
        engine.Apply(E("OpenCodeDisconnected", "", producer: "a", seq: 7));
        check(!engine.Sessions.Single(s => s.Key == "opencode:a").IsOpen && engine.Sessions.Single(s => s.Key == "opencode:b").IsOpen,
            "Producer exit closes only its own sessions");
        engine.Apply(E("OpenCodeWorking", "a", producer: "a", seq: 8));
        check(!engine.Sessions.Single(s => s.Key == "opencode:a").IsOpen, "Delayed helper cannot resurrect exited producer");
        engine.Apply(E("OpenCodeWorking", "a", producer: "new", seq: 1));
        check(engine.Sessions.Single(s => s.Key == "opencode:a").IsOpen, "New producer can resume same session ID");
        engine = new();
        engine.Apply(E("UserPromptSubmit", harness: "codex") with { OneShot = true });
        engine.Apply(E("Stop", harness: "codex") with { OneShot = true });
        check(engine.Aggregate == Activity.Idle && !engine.HasIdleSession, "codex exec ends without phantom idle light");
        engine.Apply(E("PostToolUse", harness: "codex"));
        check(!engine.HasIdleSession, "Late completion cannot reopen completed exec");
        engine = new();
        engine.Apply(E("UserPromptSubmit", harness: "claude"));
        engine.Apply(E("PreToolUse", harness: "claude") with { ToolName = "AskUserQuestion", ToolId = "q" });
        engine.Apply(E("Stop", harness: "claude"));
        engine.Apply(E("PostToolUse", harness: "claude") with { ToolName = "AskUserQuestion", ToolId = "q" });
        check(engine.Aggregate == Activity.Idle && engine.Sessions.Single().Pending.Count == 0, "Answered question after Stop releases red to idle");
        engine = new();
        engine.Apply(E("UserPromptSubmit", harness: "codex") with { OneShot = true });
        engine.Apply(E("SubagentStart", harness: "codex") with { ToolId = "child" });
        engine.Apply(E("Stop", harness: "codex"));
        engine.Apply(E("SubagentStop", harness: "codex") with { ToolId = "child" });
        check(!engine.HasIdleSession && engine.Aggregate == Activity.Idle, "Exec closes after its last child finishes");
        string title = new string('a', 239) + "🤖";
        check(StateEngine.CleanTitle(title + "z\n") == title, "Title truncation preserves full Unicode characters");
        string temp = Path.Combine(Path.GetTempPath(), "ai-light-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            string transcript = Path.Combine(temp, "rollout.jsonl");
            File.WriteAllText(transcript, """{"type":"session_meta","payload":{"id":"s","source":"exec"}}""" + "\nPRIVATE_TRANSCRIPT");
            check(HarnessMetadata.IsOneShot(transcript, "s") && !HarnessMetadata.IsOneShot(transcript, "other"), "Exec metadata binds to exact session ID");
            var parsed = EventReader.Read("codex", JsonSerializer.Serialize(new { session_id = "s", hook_event_name = "Stop", transcript_path = transcript }));
            check(parsed.OneShot && !JsonSerializer.Serialize(parsed).Contains("PRIVATE"), "Exec normalized without transcript content");
            File.WriteAllText(Path.Combine(temp, "sessions-index.json"), """{"entries":[{"sessionId":"s","customTitle":"Kendi başlığım","summary":"Özet","firstPrompt":"SECRET"}]}""");
            check(HarnessMetadata.ClaudeTitle(transcript, "s") == "Kendi başlığım" && HarnessMetadata.ClaudeTitle(transcript, "other") == "", "Claude title uses exact existing index entry");
            parsed = EventReader.Read("claude", JsonSerializer.Serialize(new { session_id = "s", hook_event_name = "Stop", transcript_path = transcript }));
            check(parsed.Title == "Kendi başlığım" && !JsonSerializer.Serialize(parsed).Contains("SECRET"), "Claude title does not forward prompt");
            File.WriteAllText(transcript, "not-json");
            check(!HarnessMetadata.IsOneShot(transcript, "s"), "Unreadable rollout gracefully remains interactive");
        } finally { Directory.Delete(temp, true); }
    }
}
