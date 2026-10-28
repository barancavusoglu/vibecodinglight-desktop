using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AiLight.Core;

namespace AiLight.App;
public static class Bridge {
    public static string PipeName => "AiLight.v1." + WindowsIdentity.GetCurrent().User!.Value
        + (Environment.GetEnvironmentVariable("AI_LIGHT_TEST_PIPE") is { Length: > 0 } test ? ".test." + test : "");
    public static async Task Send(string text, int timeout = 400) {
        using var cancel = new CancellationTokenSource(timeout);
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancel.Token);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(text + "\n"), cancel.Token);
    }
    public static async Task Listen(Action<string> received, CancellationToken cancel) {
        while (!cancel.IsCancellationRequested) {
            try {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancel);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                deadline.CancelAfter(1000);
                using var memory = new MemoryStream();
                byte[] buffer = new byte[2048];
                while (memory.Length < 16384) {
                    int n = await pipe.ReadAsync(buffer, deadline.Token);
                    if (n == 0) break;
                    memory.Write(buffer, 0, n);
                    if (Array.IndexOf(buffer, (byte)'\n', 0, n) >= 0) break;
                }
                if (memory.Length < 16384) received(Encoding.UTF8.GetString(memory.ToArray()).Trim());
            } catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException) { }
        }
    }
    public static async Task Hook(string harness, string eventName = "") {
        try {
            using var deadline = new CancellationTokenSource(1200);
            using var input = Console.OpenStandardInput();
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            while (memory.Length <= 1024 * 1024) {
                int n = await input.ReadAsync(buffer, deadline.Token);
                if (n == 0) break;
                memory.Write(buffer, 0, n);
            }
            if (memory.Length > 1024 * 1024) return;
            var (pid, started) = ProcessTree.FindOwner();
            var json = Encoding.UTF8.GetString(memory.ToArray());
            var e = HarnessCatalog.IsPreview(harness) ? ProviderHooks.Read(harness, eventName, json, pid, started)
                : EventReader.Read(harness, json, pid, started, Storage.CorrelationKey());
            if (e is null) return;
            await Send(JsonSerializer.Serialize(e));
        } catch { /* Observe only. Never deny a harness operation. */ }
        finally {
            // Antigravity parses every hook result as JSON, including failures.
            // A neutral object makes no permission or content decision.
            if (harness == "antigravity") Console.Out.WriteLine(eventName == "PreToolUse" ? "{\"decision\":\"allow\"}" : "{}");
        }
    }
}

public static class ProcessTree {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry {
        public uint Size, Usage, Pid; public IntPtr Heap; public uint Module, Threads, Parent;
        public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr h, ref Entry e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr h, ref Entry e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    public static (int Pid, long Started) FindOwner() {
        var map = new Dictionary<uint, Entry>();
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return (0, 0);
        try {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>(), Name = "" };
            if (Process32FirstW(snapshot, ref entry)) do { map[entry.Pid] = entry; } while (Process32NextW(snapshot, ref entry));
        } finally { CloseHandle(snapshot); }
        uint pid = (uint)Environment.ProcessId;
        for (int i = 0; i < 12 && map.TryGetValue(pid, out var child) && map.TryGetValue(child.Parent, out var parent); i++) {
            pid = parent.Pid;
            if (parent.Name.Equals("codex.exe", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("opencode.exe", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("copilot.exe", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("antigravity.exe", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("bun.exe", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("node.exe", StringComparison.OrdinalIgnoreCase)) {
                try { using var process = Process.GetProcessById((int)pid); return ((int)pid, process.StartTime.ToUniversalTime().Ticks); } catch { }
            }
        }
        return (0, 0);
    }
    public static bool Alive(int pid, long started) {
        if (pid == 0) return true; // Unknown is not evidence of process exit.
        try { using var p = Process.GetProcessById(pid); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == started; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch { return true; }
    }
}
