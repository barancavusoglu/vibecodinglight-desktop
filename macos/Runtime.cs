using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AiLight.Core;
using Activity = AiLight.Core.Activity;
namespace AiLight.Mac;
public sealed class Runtime(bool preview) : IDisposable {
    public Preferences Preferences { get; } = preview ? new() : Storage.Load();
    public StateEngine Engine { get; } = new();
    public MainWindow Window { get; set; } = null!;
    public bool Exiting { get; private set; }
    public DeviceConnectionState DeviceState { get; private set; } = new(DeviceTransport.None, "Cihaz aranıyor");
    public string DeviceStatus => DeviceState.Detail;
    internal void PreviewDeviceState(DeviceConnectionState state) {
        if (!preview) throw new InvalidOperationException("Device preview requires offline mode");
        DeviceState = state; Window.Refresh();
    }
    public string Notice { get; set; } = "";
    public event Action? Changed;
    public string Summary => $"{Engine.Sessions.Count(s => Engine.Included(s) && Shown(s) == Activity.Working)} çalışıyor · {Engine.Sessions.Count(s => Engine.Included(s) && Shown(s) == Activity.Idle)} yeni görev bekliyor · {Engine.Sessions.Count(s => Engine.Included(s) && Shown(s) == Activity.NeedsInput)} müdahale gerekiyor";
    public OutputMode Mode { get; private set; }
    public bool PermissionReviewHeld { get; private set; }
    public Activity Shown(Session session) => LightPresentation.SessionActivity(session, PermissionReviewHeld, Mode);
    public Activity ShownActivity => Mode == OutputMode.Red ? Activity.NeedsInput : Mode is OutputMode.Green or OutputMode.Yellow ? Activity.Working : Activity.Idle;
    public Dictionary<string, bool> Connections { get; } = [];
    public DateTime? LastConnectionCheck { get; private set; }
    public bool CheckingConnections { get; private set; }
    private readonly PermissionLightGate permissionGate = new();
    private DateTime nextHealth;
    private DateTime? idleStarted;
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private Task? worker;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private (int R, int G, int B) desired;
    private OutputMode outputMode;
    private int brightness;
    private string port = "", deviceId = "";
    private DateTime testUntil;
    private Activity testState;
    private bool testBlue;
    private bool titleRunning;
    private long nextTitles;
    public void Start() {
        if (preview) {
            Engine.Apply(new("preview1", "codex", "one", "UserPromptSubmit", "", "", "", "VibeCodingLight", "", false, 1));
            Engine.Apply(new("preview2", "opencode", "two", "SessionStart", "", "", "", "Web sitesi", "", false, 2, Title: "Web sitesi geliştirme"));
        } else {
            // Live hook events establish sessions after restart, never persisted history.
            try { Storage.Save("sessions.json", Array.Empty<Session>()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice = "Oturum geçmişi temizlenemedi: " + ex.Message; }
            if (Storage.Exe.Contains(".app/Contents/MacOS/", StringComparison.Ordinal)) {
                foreach (string family in Integrations.Families) {
                    if (Preferences.OwnedCommands.GetValueOrDefault(family)?.Count > 0 && !Integrations.Installed(Preferences, family)) {
                        try { Integrations.Change(Preferences, family, true); Notice = "Entegrasyon kayıtları onarıldı; ilgili harness'ı yeniden başlat. Codex'te değişen kayıtların güven onayını kontrol et."; }
                        catch (Exception ex) { Notice = family + ": " + ex.Message; }
                    }
                }
            }
            _ = Bridge.Listen(Program.Listener!, text => Dispatcher.UIThread.Post(() => Receive(text)), stop.Token);
            worker = Task.Run(async () => {
                using var device = new Device();
                while (!stop.IsCancellationRequested) {
                    (int R, int G, int B) rgb; OutputMode mode; int level; string selected, id;
                    lock (gate) { rgb = desired; mode = outputMode; level = brightness; selected = port; id = deviceId; }
                    await device.TickAsync(selected, id, rgb, level, mode, stop.Token); DeviceState = device.State;
                    try { await Task.Delay(50, stop.Token); } catch (OperationCanceledException) { break; }
                }
            });
        }
        timer.Tick += (_, _) => Tick(); timer.Start(); Tick();
    }
    private void Receive(string text) {
        if (text == "SHOW") { Show(); return; } if (text == "EXIT") { Exit(); return; }
        try {
            var e = JsonSerializer.Deserialize<HookEvent>(text); if (e is null) return;
            bool pending = Engine.Sessions.Any(s => s.Key == e.Harness + ":" + e.SessionId && s.Pending.Count > 0);
            Engine.Apply(e); permissionGate.Observe(e, pending, Engine, DateTime.UtcNow); Persist(); Tick();
        } catch (Exception e) when (e is JsonException or ArgumentException or NullReferenceException) { }
    }
    public void Tick() {
        if (!preview && !CheckingConnections && DateTime.UtcNow >= nextHealth) _ = CheckConnections();
        if (!preview && Engine.PruneIdleSessions(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Length > 0) Persist();
        if (!preview) RefreshTitles();
        foreach (var session in Engine.Sessions.Where(s => s.IsOpen).ToArray())
            if (!ProcessTree.Alive(session.OwnerPid, session.OwnerStart)) Engine.OwnerEnded(session.Key);
        bool testing = DateTime.UtcNow < testUntil;
        var effect = Engine.Effect;
        if (effect == LightEffect.IdleBreathing) {
            idleStarted ??= DateTime.UtcNow;
            if (StateEngine.IdleBreathingExpired(Preferences.IdleAutoOffEnabled, Preferences.IdleAutoOffMinutes, DateTime.UtcNow - idleStarted.Value)) effect = LightEffect.None;
        } else idleStarted = null;
        PermissionReviewHeld = !testing && permissionGate.Hold(Engine, Mode, DateTime.UtcNow);
        if (!PermissionReviewHeld) Mode = testing ? OutputMode.Test : effect == LightEffect.IdleBreathing ? OutputMode.Blue
            : effect == LightEffect.WorkingAndIdle ? OutputMode.Yellow : Engine.Aggregate == Activity.NeedsInput ? OutputMode.Red
            : Engine.Aggregate == Activity.Working ? OutputMode.Green : OutputMode.Off;
        lock (gate) {
            brightness = Preferences.Brightness; port = Preferences.Port; deviceId = Preferences.DeviceId;
            desired = StateEngine.Color(testing ? testState : Engine.Aggregate, brightness);
            if (testing && testBlue) desired = (0, 0, (int)Math.Round(brightness * 255.0 / 100));
            outputMode = Mode;
        }
        Window.Refresh(); Changed?.Invoke();
    }
    public async Task CheckConnections(bool manual = false) {
        if (preview || CheckingConnections) return;
        CheckingConnections = true;
        if (manual) Notice = "Bağlantılar kontrol ediliyor…";
        try {
            var result = await Task.Run(() => Integrations.Families.ToDictionary(f => f, f => Integrations.Installed(Preferences, f)));
            foreach (var (family, healthy) in result) Connections[family] = healthy;
            LastConnectionCheck = DateTime.Now;
            var broken = result.Where(p => !p.Value && Preferences.OwnedCommands.GetValueOrDefault(p.Key)?.Count > 0).Select(p => HarnessCatalog.Label(p.Key)).ToArray();
            if (broken.Length > 0) Notice = "Onarım gerekiyor · " + string.Join(", ", broken);
            else if (manual) Notice = "Bağlantı kayıtları kontrol edildi. Gerçek bağlantı için yeni görev başlat.";
        } finally { CheckingConnections = false; nextHealth = DateTime.UtcNow.AddHours(6); Window.Refresh(); }
    }
    private void RefreshTitles() {
        if (titleRunning || Environment.TickCount64 < nextTitles) return;
        nextTitles = Environment.TickCount64 + 5000; titleRunning = true;
        var ids = Engine.Sessions.Where(s => s.Harness == "codex").Select(s => s.Key[6..]).ToHashSet();
        _ = Task.Run(() => {
            IReadOnlyDictionary<string, string>? titles = null;
            try {
                string path = Path.Combine(Path.GetDirectoryName(Integrations.ConfigPath("codex"))!, "session_index.jsonl");
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (f.Length <= 16 * 1024 * 1024) { using var r = new StreamReader(f); IEnumerable<string> Lines() { string? line; int n = 0; while (++n <= 50000 && (line = r.ReadLine()) != null) yield return line; } titles = TitleIndex.Read(Lines(), ids); }
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Dispatcher.UIThread.Post(() => {
                if (titles is not null) foreach (var s in Engine.Sessions.Where(s => s.Harness == "codex")) if (titles.TryGetValue(s.Key[6..], out string? title)) s.Title = title;
                titleRunning = false;
            });
        });
    }
    public void Test(string color) { testState = color == "red" ? Activity.NeedsInput : Activity.Working; testBlue = color == "blue"; testUntil = DateTime.UtcNow.AddSeconds(3); Tick(); }
    public void Save() { try { if (!preview) Storage.Save("settings.json", Preferences); } catch (Exception e) { Notice = e.Message; } Tick(); }
    public void Persist() { try { if (!preview) Storage.Save("sessions.json", Engine.Sessions); } catch (Exception e) { Notice = e.Message; } }
    public void Change(string family, bool install) {
        if (preview) return;
        try { Integrations.Change(Preferences, family, install); Notice = install ? family == "codex" ? "Hook kuruldu. Codex'te güven onayını tamamla; yeni görev başlat." : "Entegrasyon kuruldu. Harness'ı yeniden başlatıp mesaj gönder." : "Entegrasyon kaldırıldı; yedek saklandı. Harness'ı yeniden başlat."; }
        catch (Exception e) { Notice = e.Message; Tick(); return; }
        _ = CheckConnections(); Tick();
    }
    public void Show() { Window.Show(); Window.Activate(); Tick(); }
    public async void Exit() {
        if (Exiting) return; Exiting = true; Persist(); timer.Stop(); stop.Cancel();
        if (worker is not null) await Task.WhenAny(worker, Task.Delay(2000));
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime app) app.Shutdown();
    }
    public void Dispose() { stop.Cancel(); timer.Stop(); }
}
