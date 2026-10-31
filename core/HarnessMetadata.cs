using System.Text.Json;

namespace AiLight.Core;

// Optional local metadata only. Never run an AI to name sessions or forward prompts.
public static class HarnessMetadata {
    private static bool LocalPath(string path) => Path.IsPathFullyQualified(path)
        && !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal);
    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    private static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    public static bool IsOneShot(string transcript, string sessionId) {
        try {
            if (!LocalPath(transcript) || sessionId.Length == 0) return false;
            using var file = Open(transcript); using var reader = new StreamReader(file);
            // The first metadata record is small; never scan an entire rollout.
            var line = new System.Text.StringBuilder();
            for (int n = 0; n < 16384; n++) {
                int c = reader.Read(); if (c < 0 || c == '\n') break; line.Append((char)c);
            }
            using var doc = JsonDocument.Parse(line.ToString());
            var root = doc.RootElement;
            if (Text(root, "type") != "session_meta" || !root.TryGetProperty("payload", out var payload)) return false;
            if (Text(payload, "id") != sessionId && Text(payload, "session_id") != sessionId) return false;
            return Text(payload, "source") == "exec" || Text(payload, "originator") == "codex_exec";
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return false; }
    }
    public static string ClaudeTitle(string transcript, string sessionId) {
        try {
            if (!LocalPath(transcript) || sessionId.Length == 0) return "";
            using var file = Open(Path.Combine(Path.GetDirectoryName(transcript)!, "sessions-index.json"));
            if (file.Length > 1024 * 1024) return "";
            using var doc = JsonDocument.Parse(file);
            if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return "";
            foreach (var entry in entries.EnumerateArray()) {
                if (Text(entry, "sessionId") != sessionId) continue;
                string title = Text(entry, "customTitle");
                if (string.IsNullOrWhiteSpace(title)) title = Text(entry, "summary");
                return StateEngine.CleanTitle(title);
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or InvalidOperationException) { }
        return "";
    }
}
