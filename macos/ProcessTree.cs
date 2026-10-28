using System.Diagnostics;

namespace AiLight.Mac;

public static class ProcessTree {
    public static (int Pid, long Started) FindOwner() {
        try {
            var info = new ProcessStartInfo("/bin/ps") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            // Process names only, never command arguments or prompts.
            foreach (string arg in new[] { "-axo", "pid=,ppid=,comm=" }) info.ArgumentList.Add(arg);
            using var ps = Process.Start(info)!;
            var output = ps.StandardOutput.ReadToEndAsync();
            if (!ps.WaitForExit(350)) { ps.Kill(); return (0, 0); }
            var parents = new Dictionary<int, (int Parent, string Name)>();
            foreach (string line in output.GetAwaiter().GetResult().Split('\n')) {
                string[] fields = line.Trim().Split([' ', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 3 && int.TryParse(fields[0], out int pid) && int.TryParse(fields[1], out int parent))
                    parents[pid] = (parent, Path.GetFileName(fields[2]).ToLowerInvariant());
            }
            int current = Environment.ProcessId;
            for (int n = 0; n < 16 && parents.TryGetValue(current, out var item); n++) {
                current = item.Parent;
                if (parents.TryGetValue(current, out var owner) && owner.Name is "codex" or "claude" or "opencode" or "copilot" or "antigravity" or "agy" or "bun" or "node") {
                    using var process = Process.GetProcessById(current);
                    return (current, process.StartTime.ToUniversalTime().Ticks);
                }
            }
        } catch { }
        return (0, 0);
    }
    public static bool Alive(int pid, long started) {
        if (pid <= 0 || started <= 0) return true;
        try { using var p = Process.GetProcessById(pid); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == started; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch { return true; }
    }
}
