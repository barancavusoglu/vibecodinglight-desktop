using AiLight.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class ProviderRegression {
    public static void Run(Action<bool, string> check) {
        foreach (string family in new[] { "copilot", "antigravity" }) foreach (bool windows in new[] { true, false }) {
            string command = ProviderHooks.Command(windows ? "C:\\Users\\O'Brien\\Vibe Light.exe" : "/Users/O'Brien/Vibe Light", family, windows,
                "C:\\Users\\Test\\.gemini\\antigravity\\vibecodinglight-hook.cmd");
            if (family == "antigravity" && windows)
                check(command == "C:\\Users\\Test\\.gemini\\antigravity\\vibecodinglight-hook.cmd"
                    && ProviderHooks.AntigravityWrapper("C:\\Users\\O'Brien\\Vibe Light.exe") == "@echo off\r\n\"C:\\Users\\O'Brien\\Vibe Light.exe\" --hook antigravity %~1\r\n",
                    "Antigravity uses a single-output cmd wrapper");
            string original = "{\"unrelated\":{\"enabled\":false}}";
            string installed = ProviderHooks.Edit(original, command, [], family, windows, true);
            if (family == "antigravity") {
                var toolGroup = JsonNode.Parse(installed)!["vibecodinglight"]!["PreToolUse"]![0]!;
                check(toolGroup["matcher"]?.GetValue<string>() == "ask_question"
                    && toolGroup["hooks"] is JsonArray handlers && handlers.Count == 1,
                    $"antigravity/{windows}: question hook uses grouped matcher");
            }
            check(ProviderHooks.Installed(installed, command, family, windows), $"{family}/{windows}: complete registration");
            check(ProviderHooks.Edit(installed, command, [command], family, windows, true) == installed, $"{family}/{windows}: idempotent repair");
            var root = JsonNode.Parse(installed)!;
            string key = family == "copilot" ? "hooks" : "vibecodinglight";
            root[key]!.AsObject().Remove(ProviderHooks.Events(family)[0]);
            check(!ProviderHooks.Installed(root.ToJsonString(), command, family, windows), $"{family}/{windows}: missing event unhealthy");
            check(ProviderHooks.Edit(root.ToJsonString(), command, [command], family, windows, true) == installed, $"{family}/{windows}: repairs missing event");
            root = JsonNode.Parse(installed)!; root[key]!["enabled"] = false;
            bool refused = false;
            try { ProviderHooks.Edit(root.ToJsonString(), command, [command], family, windows, true); } catch (InvalidDataException) { refused = true; }
            check(refused && !ProviderHooks.Installed(root.ToJsonString(), command, family, windows), $"{family}/{windows}: explicit opt-out preserved");
            root = JsonNode.Parse(installed)!; root[key]![ProviderHooks.Events(family)[0]]![0]!["matcher"] = "restricted";
            refused = false;
            try { ProviderHooks.Edit(root.ToJsonString(), command, [command], family, windows, true); } catch (InvalidDataException) { refused = true; }
            check(refused, $"{family}/{windows}: modified handler preserved");
            string removed = ProviderHooks.Edit(installed, command, [command], family, windows, false);
            check(!removed.Contains("--hook") && JsonNode.DeepEquals(JsonNode.Parse(removed)!["unrelated"], JsonNode.Parse(original)!["unrelated"]), $"{family}/{windows}: uninstall preserves other settings");
            string moved = command.Replace("Vibe Light", "New Light");
            check(ProviderHooks.Installed(ProviderHooks.Edit(installed, moved, [command], family, windows, true), moved, family, windows), $"{family}/{windows}: owned command migration");
        }
        var state = new StateEngine();
        HookEvent Copilot(string name, long time) => ProviderHooks.Read("copilot", name, JsonSerializer.Serialize(new { sessionId = "same", timestamp = time, cwd = "C:/work", prompt = "PRIVATE" }))!;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000;
        state.Apply(Copilot("sessionStart", now));
        check(state.Aggregate == Activity.Idle && state.Effect == LightEffect.IdleBreathing, "Copilot opening chat is idle");
        var prompt = Copilot("userPromptSubmitted", now + 1); state.Apply(prompt);
        check(state.Aggregate == Activity.Working, "Copilot prompt starts work");
        check(!JsonSerializer.Serialize(prompt).Contains("PRIVATE"), "Copilot payload is content-free");
        state.Apply(Copilot("agentStop", now + 2)); state.Apply(prompt);
        check(state.Aggregate == Activity.Idle, "Copilot duplicate prompt cannot restart finished turn");
        state.Apply(Copilot("userPromptSubmitted", now + 3)); state.Apply(Copilot("agentStop", now + 2));
        check(state.Aggregate == Activity.Working && state.Sessions.Count == 1, "Copilot stale stop rejected, same identity retained");
        check(ProviderHooks.Read("copilot", "permissionRequest", "{}") is null, "Automatic permission checks never become red");
        check(ProviderHooks.Read("copilot", "sessionStart", "{\"cwd\":\"same-folder\"}") is null, "Missing identity cannot synthesize a session");
        check(ProviderHooks.Read("copilot", "userPromptSubmitted", "{\"session_id\":\"local\"}")?.SessionId == "local", "VS Code snake-case identity");
        HookEvent? Ag(string name, string fields = "") => ProviderHooks.Read("antigravity", name, "{\"conversationId\":\"same\",\"workspacePaths\":[\"/work/robot\"],\"modelName\":\"private\"" + fields + "}");
        state.Apply(Ag("SessionStart")!);
        check(state.Sessions.Count == 2 && state.Effect == LightEffect.WorkingAndIdle, "Providers don't merge matching IDs; working+idle is yellow");
        state.Apply(Ag("PreInvocation")!);
        check(state.Sessions.Single(s => s.Harness == "antigravity").Activity == Activity.Working, "Antigravity invocation starts work");
        check(Ag("Stop", ",\"fullyIdle\":false") is null && Ag("Stop") is null, "Antigravity background work or missing final signal is not idle");
        state.Apply(Ag("Stop", ",\"fullyIdle\":true")!);
        check(state.Sessions.Single(s => s.Harness == "antigravity").Activity == Activity.Idle, "Antigravity fullyIdle completion");
        check(Ag("PreToolUse", ",\"toolCall\":{\"name\":\"ask_permission\"}") is null, "Antigravity permission candidate not treated as user wait");
        HookEvent? Question(string name, int step, string tool = "ask_question") => ProviderHooks.Read("antigravity", name,
            JsonSerializer.Serialize(new { conversationId = "question-test", workspacePaths = new[] { "/work/robot" }, stepIdx = step,
                toolCall = new { name = tool, args = new { questions = new[] { "PRIVATE QUESTION" } } } }));
        var questionState = new StateEngine();
        questionState.Apply(ProviderHooks.Read("antigravity", "SessionStart", "{\"conversationId\":\"question-test\"}")!);
        questionState.Apply(ProviderHooks.Read("antigravity", "PreInvocation", "{\"conversationId\":\"question-test\"}")!);
        var asked = Question("PreToolUse", 4)!;
        check(asked.EventName == "ProviderAsked" && asked.ToolId == "step:4" && !JsonSerializer.Serialize(asked).Contains("PRIVATE QUESTION"),
            "Antigravity question payload retains only step identity");
        questionState.Apply(asked);
        check(questionState.Aggregate == Activity.NeedsInput, "Antigravity question turns red");
        questionState.Apply(Question("PreToolUse", 5)!);
        questionState.Apply(Question("PostToolUse", 4)!);
        check(questionState.Aggregate == Activity.NeedsInput, "Another pending Antigravity question stays red");
        questionState.Apply(ProviderHooks.Read("antigravity", "Stop", "{\"conversationId\":\"question-test\",\"fullyIdle\":true}")!);
        check(questionState.Aggregate == Activity.NeedsInput, "Antigravity stop preserves unanswered question");
        questionState.Apply(Question("PostToolUse", 5)!);
        check(questionState.Aggregate == Activity.Idle, "Antigravity answer releases red to idle");
        check(Question("PreToolUse", 6, "ask_permission") is null
            && ProviderHooks.Read("antigravity", "PreToolUse", "{\"conversationId\":\"question-test\",\"toolCall\":{\"name\":\"ask_question\"}}") is null,
            "Antigravity unrelated tools or missing step do not create red");
        var empty = new StateEngine(); empty.Apply(Ag("Stop", ",\"fullyIdle\":true")!);
        check(empty.Sessions.Count == 0, "Orphan completion does not create ghost session");
        state.ToggleLight("antigravity:same");
        check(state.Effect == LightEffect.None, "Preview provider supports shared exclusion");
        state.Apply(Copilot("sessionEnd", now + 4));
        check(state.Aggregate == Activity.Idle, "Copilot session end releases light");
    }
}
