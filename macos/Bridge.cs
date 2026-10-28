using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AiLight.Core;
namespace AiLight.Mac;
public static class Bridge {
    public static string PathName => Path.Combine(Storage.IpcRoot, "events.sock");
    public static async Task<bool> Send(string text) {
        try {
            using var timeout = new CancellationTokenSource(500);
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(PathName), timeout.Token);
            using var stream = new NetworkStream(socket);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(text + "\n"), timeout.Token);
            return true;
        } catch { return false; }
    }
    public static async Task Hook(string family, string eventName = "") {
        try {
            if (!HarnessCatalog.Families.Contains(family)) return;
            using var timeout = new CancellationTokenSource(1200);
            using var input = Console.OpenStandardInput(); using var buffer = new MemoryStream();
            byte[] bytes = new byte[8192];
            while (buffer.Length <= 1024 * 1024) {
                int n = await input.ReadAsync(bytes, timeout.Token); if (n == 0) break; buffer.Write(bytes, 0, n);
            }
            if (buffer.Length > 1024 * 1024) return;
            var (pid, started) = ProcessTree.FindOwner();
            var json = Encoding.UTF8.GetString(buffer.ToArray());
            var e = HarnessCatalog.IsPreview(family) ? ProviderHooks.Read(family, eventName, json, pid, started)
                : EventReader.Read(family, json, pid, started, Storage.Key());
            if (e is null) return;
            await Send(JsonSerializer.Serialize(e));
        } catch { /* Never prevent the harness operation. */ }
        finally {
            if (family == "antigravity") Console.Out.WriteLine(eventName == "PreToolUse" ? "{\"decision\":\"allow\"}" : "{}");
        }
    }
    // Caller must hold the lifetime lock before removing a stale socket.
    public static Socket Bind() {
        Storage.PrivateDirectory(Storage.IpcRoot);
        if (Encoding.UTF8.GetByteCount(PathName) >= 104) throw new IOException("IPC yolu macOS sınırını aşıyor.");
        if (File.Exists(PathName)) File.Delete(PathName);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(PathName)); listener.Listen(32); return listener;
    }
    public static async Task Listen(Socket listener, Action<string> receive, CancellationToken stop) {
        while (!stop.IsCancellationRequested) {
            try {
                using var socket = await listener.AcceptAsync(stop);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop); deadline.CancelAfter(1000);
                using var stream = new NetworkStream(socket); using var memory = new MemoryStream(); byte[] bytes = new byte[2048];
                while (memory.Length < 16384) {
                    int n = await stream.ReadAsync(bytes, deadline.Token); if (n == 0) break;
                    memory.Write(bytes, 0, n); if (Array.IndexOf(bytes, (byte)'\n', 0, n) >= 0) break;
                }
                if (memory.Length < 16384) receive(Encoding.UTF8.GetString(memory.ToArray()).Trim());
            } catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
}
