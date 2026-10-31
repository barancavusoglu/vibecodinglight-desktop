using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiLight.Core;

// Observe-only lifecycle adapters. No permission decisions, prompts, arguments or
// transcript contents leave the helper. The missing wait/resume pair is explicitly
// a capability limitation, not a timer-based approximation of human interaction.
public static class ProviderHooks {
    public static string[] Events(string family) => family switch {
        "copilot" => ["sessionStart", "userPromptSubmitted", "agentStop", "sessionEnd"],
        "antigravity" => ["SessionStart", "PreInvocation", "PreToolUse", "PostToolUse", "Stop"],
        _ => throw new ArgumentException("Bilinmeyen entegrasyon")
    };
    public static string PathFor(string family, string home) => family switch {
        "copilot" => Path.Combine(Environment.GetEnvironmentVariable("COPILOT_HOME") ?? Path.Combine(home, ".copilot"), "hooks", "vibecodinglight.json"),
        "antigravity" => Path.Combine(home, ".gemini", "config", "hooks.json"),
        _ => throw new ArgumentException("Bilinmeyen entegrasyon")
    };
    public static string AntigravityWrapperPath(string home) => Path.Combine(home, ".gemini", "antigravity", "vibecodinglight-hook.cmd");
    public static string AntigravityWrapper(string exe) => "@echo off\r\n\"" + exe.Replace("%", "%%") + "\" --hook antigravity %~1\r\n";
    public static string Command(string exe, string family, bool windows, string? antigravityWrapperPath = null) {
        if (!HarnessCatalog.IsPreview(family)) throw new ArgumentException("Bilinmeyen entegrasyon");
        if (!windows) return "'" + exe.Replace("'", "'\"'\"'") + "' --hook " + family;
        if (family == "antigravity") {
            string wrapper = antigravityWrapperPath ?? AntigravityWrapperPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (wrapper.Any(char.IsWhiteSpace) || wrapper.IndexOfAny(['"', '&', '|', '<', '>', '^']) >= 0)
                throw new InvalidOperationException("Antigravity hook yolu cmd.exe için güvenli değil.");
            return wrapper;
        }
        // The event argument belongs inside the PowerShell script (see Action).
        return "& '" + exe.Replace("'", "''") + "' --hook " + family;
    }
    private static string Invocation(string command, string name, bool windows, string family) {
        string script = command + " " + name;
        if (!windows || family == "copilot") return script;
        // Antigravity invokes commands through cmd.exe; a quoted executable path
        // avoids cmd/PowerShell nested quoting and preserves the stdin pipe.
        return script;
    }
    private static JsonObject Action(string command, string name, bool windows, string family) {
        if (family == "copilot")
            return new() { ["type"] = "command", [windows ? "powershell" : "bash"] = Invocation(command, name, windows, family), ["timeoutSec"] = 2 };
        var handler = new JsonObject { ["type"] = "command", ["command"] = Invocation(command, name, windows, family), ["timeout"] = 2 };
        return name is "PreToolUse" or "PostToolUse"
            ? new() { ["matcher"] = "ask_question", ["hooks"] = new JsonArray(handler) }
            : handler;
    }
    private static JsonObject Parse(string input) => JsonNode.Parse(input, documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 }) as JsonObject
        ?? throw new InvalidDataException("Hook dosyası JSON nesnesi olmalı.");
    private static JsonObject? Block(JsonObject root, string family) => root[family == "copilot" ? "hooks" : "vibecodinglight"] as JsonObject;
    public static string Edit(string input, string command, IReadOnlyCollection<string> owned, string family, bool windows, bool install) {
        var root = Parse(input);
        string key = family == "copilot" ? "hooks" : "vibecodinglight";
        if (root[key] is not null && root[key] is not JsonObject) throw new InvalidDataException("Hook yapısı tanınmıyor; dosya korundu.");
        if (family == "copilot" && root["version"] is not null && root["version"]!.ToJsonString() != "1") throw new InvalidDataException("Copilot hook sürümü tanınmıyor; dosya korundu.");
        var block = Block(root, family) ?? new JsonObject();
        if (install && (block["enabled"]?.ToJsonString() == "false" || root["enabled"]?.ToJsonString() == "false"))
            throw new InvalidDataException("Hook kullanıcı tarafından kapatılmış; önce harness ayarından etkinleştir.");
        // A reserved namespace is not ownership evidence. Refuse to overwrite any
        // modified or foreign handler, including custom matchers and unknown keys.
        var known = owned.Append(command).ToArray();
        foreach (var entry in block) {
            if (entry.Key == "enabled") continue;
            if (!Events(family).Contains(entry.Key) || entry.Value is not JsonArray actions ||
                actions.Any(a => !known.Any(c => JsonNode.DeepEquals(a, Action(c, entry.Key, windows, family)))))
                throw new InvalidDataException("VibeCodingLight kayıt alanı değiştirilmiş; kullanıcı kayıtları korundu.");
        }
        if (!install) root.Remove(key);
        else {
            var next = new JsonObject();
            foreach (var name in Events(family)) next[name] = new JsonArray(Action(command, name, windows, family));
            root[key] = next;
            if (family == "copilot") root["version"] = 1;
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    public static bool Installed(string input, string command, string family, bool windows) {
        try {
            var root = Parse(input); var block = Block(root, family);
            if (block is null || block["enabled"]?.ToJsonString() == "false" || root["enabled"]?.ToJsonString() == "false") return false;
            if (family == "copilot" && root["version"]?.ToJsonString() != "1") return false;
            return Events(family).All(name => block[name] is JsonArray actions && actions.Count == 1 && JsonNode.DeepEquals(actions[0], Action(command, name, windows, family)));
        } catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException) { return false; }
    }
    public static HookEvent? Read(string family, string name, string json, int pid = 0, long started = 0) {
        if (!Events(family).Contains(name)) return null;
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        string S(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        string id = family == "copilot" ? S("sessionId") : S("conversationId");
        // Local VS Code uses snake_case and may omit identity. Never merge by cwd.
        if (id.Length == 0 && family == "copilot") id = S("session_id");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256) return null;
        string normalized = name switch {
            "sessionStart" or "SessionStart" => "SessionStart",
            "userPromptSubmitted" => "UserPromptSubmit",
            "PreInvocation" => "ProviderWorking",
            "PreToolUse" when family == "antigravity" => "ProviderAsked",
            "PostToolUse" when family == "antigravity" => "ProviderReplied",
            "agentStop" => "ProviderIdle",
            "sessionEnd" => "SessionEnd",
            "Stop" when root.TryGetProperty("fullyIdle", out var idle) && idle.ValueKind == JsonValueKind.True => "ProviderIdle",
            _ => ""
        };
        if (normalized.Length == 0) return null; // fullyIdle=false/missing isn't completion.
        string toolId = "";
        string toolName = "";
        if (name is "PreToolUse" or "PostToolUse") {
            if (!root.TryGetProperty("toolCall", out var call) || call.ValueKind != JsonValueKind.Object
                || !call.TryGetProperty("name", out var callName) || callName.ValueKind != JsonValueKind.String
                || callName.GetString() != "ask_question"
                || !root.TryGetProperty("stepIdx", out var step) || step.ValueKind != JsonValueKind.Number
                || !step.TryGetInt64(out long index) || index < 0) return null;
            toolId = "step:" + index;
            toolName = "ask_question";
        }
        string cwd = S("cwd");
        if (family == "antigravity" && root.TryGetProperty("workspacePaths", out var paths) && paths.ValueKind == JsonValueKind.Array)
            cwd = paths.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.String ? paths[0].GetString() ?? "" : "";
        long time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string eventId = Guid.NewGuid().ToString("N");
        if (root.TryGetProperty("timestamp", out var ts)) {
            long parsed = ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var n) ? n
                : ts.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ts.GetString(), out var date) ? date.ToUnixTimeMilliseconds() : 0;
            if (parsed > 0 && parsed <= time + 60_000) {
                time = parsed;
                eventId = family + ":" + id + ":" + name + ":" + parsed;
            }
        }
        return new(eventId, family, id, normalized, "", toolId, toolName, StateEngine.CleanTitle(cwd.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault() ?? ""), "", false, time, pid, started);
    }
}
