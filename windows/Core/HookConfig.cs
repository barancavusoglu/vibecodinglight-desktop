using System.Text.Json;
using System.Text.Json.Nodes;
namespace AiLight.Core;

public static class HookConfig {
    public static string WindowsCommand(string executable, string harness) {
        if (harness is not ("codex" or "claude")) throw new ArgumentException("Bilinmeyen entegrasyon");
        // A quoted executable alone is not an invocation in PowerShell.
        // Prefer a readable command in the trust UI. Rare expansion characters
        // need encoding because the outer shell may be either cmd or PowerShell.
        if (harness == "codex") {
            string script = $"& '{executable.Replace("'", "''")}' --hook codex";
            if (executable.IndexOfAny(['$', '`', '%', '!', '"', '\r', '\n']) < 0)
                return "powershell.exe -NoProfile -NonInteractive -Command \"" + script + "\"";
            return "powershell.exe -NoProfile -NonInteractive -EncodedCommand "
                + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        }
        return $"\"{executable}\" --hook {harness}";
    }
    public static readonly string[] CommonEvents = ["SessionStart", "UserPromptSubmit", "PreToolUse", "PermissionRequest", "PostToolUse", "Stop", "SessionEnd", "SubagentStart", "SubagentStop"];
    public static IEnumerable<string> Events(string harness) => harness switch {
        "claude" => CommonEvents.Concat(["Notification", "PostToolUseFailure", "StopFailure"]),
        "codex" => CommonEvents.Concat(["Interrupt"]),
        _ => throw new ArgumentException("Bilinmeyen entegrasyon")
    };
    private static JsonObject Parse(string input) => JsonNode.Parse(input, documentOptions: new JsonDocumentOptions {
        AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
    }) as JsonObject ?? throw new InvalidDataException("Ayar dosyası bir JSON nesnesi olmalı.");
    private static string? Command(JsonNode? action) => action is JsonObject a && a["command"] is JsonValue v
        && v.TryGetValue<string>(out var value) ? value : null;
    private static bool Unrestricted(JsonObject group) => group["matcher"] is null
        || group["matcher"] is JsonValue v && v.TryGetValue<string>(out var matcher) && matcher is "" or "*";
    private static JsonObject Action(string command) => new() { ["type"] = "command", ["command"] = command, ["timeout"] = 2 };
    public static IReadOnlyList<string> Issues(string input, string command, string harness) {
        try {
            var root = Parse(input);
            if (root["disableAllHooks"] is JsonValue flag && flag.TryGetValue<bool>(out var disabled) && disabled)
                return ["Tüm hook'lar kullanıcı ayarında kapalı"];
            var issues = new List<string>();
            foreach (string name in Events(harness)) {
                var groups = root["hooks"]?[name] as JsonArray;
                var owned = groups?.OfType<JsonObject>().Where(g => g["hooks"] is JsonArray)
                    .SelectMany(g => g["hooks"]!.AsArray().Where(a => Command(a) == command).Select(a => (Group: g, Action: a))).ToList();
                if (owned is null || owned.Count == 0) issues.Add(name + " eksik/eski");
                else if (owned.Count > 1) issues.Add(name + " yinelenmiş");
                else if (!Unrestricted(owned[0].Group) || owned[0].Action?["type"]?.GetValue<string>() != "command") issues.Add(name + " kapsamı/türü hatalı");
            }
            return issues;
        } catch (Exception e) when (e is JsonException or InvalidOperationException or InvalidDataException) { return ["Hook dosyası biçimi geçersiz"]; }
    }

    // Presence of one command is not proof that all lifecycle events are wired.
    public static bool IsInstalled(string input, string command, string harness) {
        try {
            var root = Parse(input);
            if (root["disableAllHooks"] is JsonValue flag && flag.TryGetValue<bool>(out var disabled) && disabled) return false;
            return Events(harness).All(name => root["hooks"]?[name] is JsonArray groups
                && groups.OfType<JsonObject>().Where(g => g["hooks"] is JsonArray)
                    .SelectMany(g => g["hooks"]!.AsArray()).Count(a => Command(a) == command) == 1
                && groups.Any(g => g is JsonObject group && Unrestricted(group) && group["hooks"] is JsonArray actions
                    && actions.Any(a => Command(a) == command && a?["type"]?.GetValue<string>() == "command")));
        } catch (Exception e) when (e is JsonException or InvalidOperationException or InvalidDataException) { return false; }
    }
    public static string Edit(string input, string command, IReadOnlyCollection<string> ownedCommands, string harness, bool install) {
        var required = Events(harness).ToHashSet(StringComparer.Ordinal);
        var root = Parse(input);
        if (install && root["disableAllHooks"] is JsonValue flag && flag.TryGetValue<bool>(out var disabled) && disabled)
            throw new InvalidDataException("Harness ayarlarında tüm hook'lar kapalı. Önce bu ayarı etkinleştir.");
        if (root["hooks"] is not null && root["hooks"] is not JsonObject) throw new InvalidDataException("hooks yapısı tanınmıyor; dosya korunuyor.");
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        var owned = ownedCommands.Append(command).ToHashSet(StringComparer.Ordinal);
        bool Ours(JsonNode? action) => Command(action) is string c && owned.Contains(c);
        var installed = new HashSet<string>();
        foreach (var pair in hooks.ToList()) {
            if (pair.Value is not JsonArray groups) throw new InvalidDataException("Hook listesi tanınmıyor; dosya korunuyor.");
            for (int i = 0; i < groups.Count; i++) {
                if (groups[i] is not JsonObject group || group["hooks"] is not JsonArray actions) continue;
                bool allOwned = actions.Count > 0 && actions.All(Ours);
                bool keep = install && required.Contains(pair.Key) && Unrestricted(group) && !installed.Contains(pair.Key);
                if (allOwned) {
                    if (keep) {
                        group["hooks"] = new JsonArray(Action(command)); installed.Add(pair.Key);
                    } else groups[i] = new JsonObject { ["hooks"] = new JsonArray() };
                    continue;
                }
                // Shared groups keep foreign action indices as well as group indices.
                // Updating in place is safe; an interleaved removal cannot be made
                // inert without adding a new trusted command, so leave the file intact.
                for (int j = actions.Count - 1; j >= 0; j--) {
                    if (!Ours(actions[j])) continue;
                    if (keep) { actions[j] = Action(command); installed.Add(pair.Key); keep = false; }
                    else if (harness == "codex" && actions.Skip(j + 1).Any(a => !Ours(a)))
                        throw new InvalidDataException("VibeCodingLight hook'u başka bir hook ile aynı gruba karıştırılmış. Diğer güven onaylarını korumak için dosya değiştirilmedi; VibeCodingLight kaydını ayrı gruba taşı.");
                    else actions.RemoveAt(j);
                }
            }
            // Empty groups are inert. Keep interior slots so Codex trust positions
            // survive install/update/uninstall; only trim truly empty trailing slots.
            while (groups.Count > 0 && Placeholder(groups[^1])) groups.RemoveAt(groups.Count - 1);
            if (groups.Count == 0) hooks.Remove(pair.Key);
        }
        if (install) {
            foreach (string name in required.Where(n => !installed.Contains(n))) {
                var groups = hooks[name] as JsonArray;
                if (groups is null) { groups = new JsonArray(); hooks[name] = groups; }
                var replacement = new JsonObject { ["hooks"] = new JsonArray(Action(command)) };
                int slot = Enumerable.Range(0, groups.Count).FirstOrDefault(i => Placeholder(groups[i]), -1);
                if (slot >= 0) groups[slot] = replacement; else groups.Add(replacement);
            }
        }
        if (root["hooks"] is null && hooks.Count > 0) root["hooks"] = hooks;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    private static bool Placeholder(JsonNode? node) => node is JsonObject group && group.Count == 1
        && group["hooks"] is JsonArray { Count: 0 };
}
