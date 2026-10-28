using System.IO;
using AiLight.Core;

namespace AiLight.App;

public sealed class CodexApprovalWatcher {
    private readonly HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> pendingRoots = new();
    private readonly Dictionary<string, long> offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool?> reviewFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> reviewParents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CodexApprovalFeed> feeds = new(StringComparer.OrdinalIgnoreCase);

    public CodexApprovalWatcher(string? sessionsRoot = null) {
        string home = Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        ObserveRoot(sessionsRoot ?? Environment.GetEnvironmentVariable("AI_LIGHT_CODEX_SESSIONS_DIR")
            ?? Path.Combine(home, "sessions"));
    }

    public void ObserveRoot(string path) { if (!string.IsNullOrWhiteSpace(path)) pendingRoots.Enqueue(path); }

    public async Task Run(Action<string, string> resolved, CancellationToken cancel,
        Action<string>? status = null, Action<string>? started = null) {
        try {
            DrainRoots();
            status?.Invoke(roots.Count == 0 ? "missing" : "ready");
            while (!cancel.IsCancellationRequested) {
                DrainRoots();
                foreach (string path in Files()) {
                    if (!offsets.ContainsKey(path)) { offsets[path] = 0; reviewFiles[path] = null; }
                    if (reviewFiles[path] is null) reviewFiles[path] = IsReviewFile(path);
                    if (reviewFiles[path] == true && !feeds.ContainsKey(path)) Prime(path);
                    if (reviewFiles[path] == true) ReadNew(path, resolved, started);
                }
                await Task.Delay(250, cancel);
            }
        } catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status?.Invoke("error"); }
    }

    private void DrainRoots() {
        while (pendingRoots.TryDequeue(out string? root)) {
            if (!Directory.Exists(root) || !roots.Add(root)) continue;
            foreach (string path in Files(root)) {
                try {
                    offsets[path] = new FileInfo(path).Length; reviewFiles[path] = IsReviewFile(path);
                    if (reviewFiles[path] == true) Prime(path);
                }
                catch (IOException) { }
            }
        }
    }
    private void Prime(string path) {
        var parser = feeds.GetValueOrDefault(path);
        if (parser is null) feeds[path] = parser = new CodexApprovalFeed();
        try {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file, System.Text.Encoding.UTF8, true, 4096, leaveOpen: false);
            string? first = reader.ReadLine();
            if (first is not null) {
                parser.Push(first);
                using var document = System.Text.Json.JsonDocument.Parse(first);
                var payload = document.RootElement.GetProperty("payload");
                if (payload.TryGetProperty("parent_thread_id", out var parent)
                    && parent.ValueKind == System.Text.Json.JsonValueKind.String)
                    reviewParents[path] = parent.GetString() ?? "";
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException) { }
    }

    private static bool? IsReviewFile(string path) {
        try {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (file.Length == 0) return null;
            byte[] prefix = new byte[Math.Min(file.Length, 64 * 1024)];
            int read = file.Read(prefix, 0, prefix.Length);
            string text = System.Text.Encoding.UTF8.GetString(prefix, 0, read);
            return text.Contains("\"thread_source\":\"guardian_review\"", StringComparison.Ordinal)
                || text.Contains("\"other\":\"guardian\"", StringComparison.Ordinal)
                || text.Contains("\"model\":\"codex-auto-review\"", StringComparison.Ordinal);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private IEnumerable<string> Files() {
        try { return roots.SelectMany(Files).ToArray(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { return []; }
    }
    private static IEnumerable<string> Files(string root) {
        try { return Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).ToArray(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { return []; }
    }

    private bool ReadNew(string path, Action<string, string> resolved, Action<string>? started) {
        try {
            long offset = offsets.GetValueOrDefault(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (file.Length < offset) offset = 0;
            long available = file.Length - offset;
            if (available <= 0) { offsets[path] = file.Length; return false; }
            if (available > 8 * 1024 * 1024) offset = file.Length - 8 * 1024 * 1024;
            file.Position = offset;
            byte[] bytes = new byte[file.Length - offset];
            int total = 0;
            while (total < bytes.Length) {
                int read = file.Read(bytes, total, bytes.Length - total);
                if (read == 0) break;
                total += read;
            }
            int complete = Array.LastIndexOf(bytes, (byte)'\n', total - 1) + 1;
            if (complete == 0) return false;
            offsets[path] = offset + complete;
            string text = System.Text.Encoding.UTF8.GetString(bytes, 0, complete);
            var parser = feeds.GetValueOrDefault(path);
            if (parser is null) feeds[path] = parser = new CodexApprovalFeed();
            foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
                if (started is not null && reviewParents.TryGetValue(path, out string? parent)
                    && parent.Length > 0 && IsReviewStart(line)) started(parent);
                var result = parser.Push(line.TrimEnd('\r'));
                if (result is { } decision) resolved(decision.SessionId, decision.Outcome);
            }
            return true;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
    private static bool IsReviewStart(string line) {
        try {
            using var document = System.Text.Json.JsonDocument.Parse(line);
            var root = document.RootElement;
            return root.TryGetProperty("payload", out var payload)
                && payload.TryGetProperty("type", out var type)
                && type.ValueKind == System.Text.Json.JsonValueKind.String
                && type.GetString() == "task_started";
        } catch (System.Text.Json.JsonException) { return false; }
    }
}
