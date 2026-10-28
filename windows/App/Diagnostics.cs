using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AiLight.Core;
namespace AiLight.App;
public record DiagnosticState(string Light, string Session, int Pending);
public record DiagnosticEntry(DateTimeOffset Time, string Event, string Harness, string SessionTag,
    string RequestTag, string CommandTag, string TurnTag, string Source, DiagnosticState Before, DiagnosticState After,
    int Brightness, bool Testing, bool Connected);
public sealed class Diagnostics {
    public const int Capacity = 500;
    public const string FileName = "diagnostics.json";
    private readonly object gate = new();
    private readonly List<DiagnosticEntry> entries = [];
    private readonly Channel<bool> writes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task worker;
    private readonly bool persist;
    public string Error { get; private set; } = "";
    public int Revision { get; private set; }
    public Diagnostics(bool persist = true) {
        this.persist = persist;
        if (persist) {
            try {
                var file = new System.IO.FileInfo(System.IO.Path.Combine(Storage.Root, FileName));
                if (file.Exists && file.Length < 2_000_000) entries.AddRange((Storage.Read<List<DiagnosticEntry>>(FileName) ?? []).TakeLast(Capacity));
            } catch { Error = "Önceki tanı kaydı okunamadı."; }
        }
        worker = Task.Run(async () => {
            await foreach (var _ in writes.Reader.ReadAllAsync()) {
                try { if (persist) Storage.Save(FileName, Snapshot()); Error = ""; }
                catch { Error = "Tanı günlüğü diske yazılamadı; kayıtlar şimdilik bellekte."; }
            }
        });
    }
    public static DiagnosticState Capture(StateEngine engine, string? key = null) {
        var s = engine.Sessions.FirstOrDefault(s => s.Key == key);
        string light = engine.Aggregate == Activity.NeedsInput ? "Kırmızı" : engine.Effect == LightEffect.WorkingAndIdle ? "Sarı"
            : engine.Aggregate == Activity.Working ? "Yeşil" : engine.HasIdleSession ? "Mavi nefes" : "Kapalı";
        return new(light, s?.Activity.ToString() ?? "Yok", s?.Pending.Count ?? 0);
    }
    private static string Tag(string value) => value.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
    private static readonly HashSet<string> Events = new(HookConfig.CommonEvents.Concat(new[] {
        "Interrupt", "Notification", "PostToolUseFailure", "StopFailure", "OpenCodeMetadata", "OpenCodeWorking", "OpenCodeIdle", "OpenCodeError", "OpenCodeAsked", "OpenCodeReplied", "OpenCodeDisconnected",
        "ProviderWorking", "ProviderIdle", "PermissionAutoResolved", "Startup", "OwnerEnded", "SessionRemoved", "SessionExpired", "ColorTestStarted", "ColorTestEnded", "SettingsChanged", "Exit"
    }));
    public void Record(string eventName, string harness, string sessionId, DiagnosticState before, DiagnosticState after,
        int brightness, bool testing, bool connected, string requestId = "", string correlation = "", string turnId = "", string source = "") {
        string safeSource = source is "startup" or "resume" or "clear" or "compact" or "allow" or "deny" ? source : "";
        var entry = new DiagnosticEntry(DateTimeOffset.UtcNow, Events.Contains(eventName) ? eventName : "OtherEvent",
            HarnessCatalog.Families.Contains(harness) ? harness : "app", Tag(sessionId),
            Tag(requestId), Tag(correlation), Tag(turnId), safeSource,
            before, after, brightness, testing, connected);
        lock (gate) { entries.Add(entry); if (entries.Count > Capacity) entries.RemoveRange(0, entries.Count - Capacity); Revision++; }
        if (persist) writes.Writer.TryWrite(true);
    }
    public DiagnosticEntry[] Snapshot() { lock (gate) return entries.ToArray(); }
    public void Clear() { lock (gate) { entries.Clear(); Revision++; } if (persist) writes.Writer.TryWrite(true); }
    public async Task Complete() { writes.Writer.TryComplete(); await worker; }
}
