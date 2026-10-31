using System.Text.Json;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AiLight.Core;
using Activity = AiLight.Core.Activity;
namespace AiLight.Desktop;

public sealed class Runtime(bool preview) : IDisposable
{
    public DesktopPreferences Preferences { get; } = preview ? new() : Storage.Load();
    public StateEngine Engine { get; } = new();
    public SessionCatalog Titles { get; } = new(() => Path.Combine(Path.GetDirectoryName(Integrations.ConfigPath("codex"))!, "session_index.jsonl"));
    public DiagnosticJournal Diagnostics { get; } = new(!preview,
        () => new FileInfo(Path.Combine(Storage.Root, DiagnosticJournal.FileName)) is { Exists: true, Length: < 2_000_000 }
            ? Storage.Read<List<DiagnosticEntry>>(DiagnosticJournal.FileName) ?? [] : [],
        entries => Storage.Save(DiagnosticJournal.FileName, entries));
    public MainWindow Window { get; set; } = null!;
    public bool Exiting { get; private set; }
    public DeviceConnectionState DeviceState { get; private set; } = new(DeviceTransport.None, "Cihaz aranıyor");
    public string DeviceStatus => DeviceState.Detail;
    public string Notice { get; set; } = "";
    public event Action? Changed;
    public string Summary => PermissionReviewHeld ? "İzin kontrol ediliyor · ışık durumu korunuyor" : $"{Engine.Sessions.Count(s => Engine.Included(s) && Shown(s) == Activity.Working)} çalışıyor · {Engine.Sessions.Count(s => Engine.Included(s) && Shown(s) == Activity.Idle)} yeni görev bekliyor · {Engine.Sessions.Count(s => Engine.Included(s) && Shown(s) == Activity.NeedsInput)} müdahale gerekiyor";
    public OutputMode Mode { get; private set; }
    public bool PermissionReviewHeld { get; private set; }
    public Activity Shown(Session s) => LightPresentation.SessionActivity(s, PermissionReviewHeld, Mode);
    public Activity ShownActivity => Mode == OutputMode.Red ? Activity.NeedsInput : Mode is OutputMode.Green or OutputMode.Yellow ? Activity.Working : Activity.Idle;
    public Dictionary<string, ConnectionCheck> Connections { get; } = [];
    public DateTime? LastConnectionCheck { get; private set; }
    public bool CheckingConnections { get; private set; }
    public string HealthWarning => string.Join(", ", Connections.Values.Where(c => c.Enabled && !c.Healthy).Select(c => HarnessCatalog.Label(c.Family))) is { Length: > 0 } names ? names + " · kayıtları kontrol et / onar." : "";
    public bool HasObservedConnection(string family) => observed.Has(family);
    public string ConnectionAppearance(string family) => Connections.TryGetValue(family, out var check)
        ? !check.Enabled ? "disabled" : !check.Healthy ? "unhealthy" : observed.Has(family) ? "healthy" : "pending"
        : Preferences.OwnedCommands.GetValueOrDefault(family)?.Count > 0 ? "pending" : "disabled";
    public string ConnectionStatus(string family) => !Connections.TryGetValue(family, out var check) ? "Kontrol bekleniyor" : !check.Enabled ? "Etkin değil" : !check.Healthy ? check.Detail : check.Detail + (observed.Has(family) ? " · gerçek olay alındı" : " · gerçek olay bekleniyor. Güven onayını kontrol et; yeni oturumda mesaj gönder.");
    private readonly ConnectionObservations observed = new();
    private readonly PermissionLightGate permissionGate = new();
    private readonly ManualLightPreview lightPreview = new();
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer testExpiryTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer permissionGraceTimer = new() { Interval = PermissionLightGate.Grace };
    private Task? worker, approvalTask, connectionCheck;
    private CodexApprovalWatcher? approvalWatcher;
    private DateTime nextHealth;
    private DateTime? idleStarted;
    private string? hookNoticeFamily;
    private bool wasTesting;
    private (int R, int G, int B) desired;
    private OutputMode outputMode;
    private int brightness;
    private string port = "", deviceId = "";
    internal (int R, int G, int B) OutputRgb { get { lock (gate) return desired; } }
    public bool Testing => lightPreview.IsActive;
    public bool PreviewingBrightness => lightPreview.IsBrightness;
    internal void PreviewDeviceState(DeviceConnectionState state)
    {
        if (!preview) throw new InvalidOperationException("Device preview requires offline mode");
        DeviceState = state; Window.Refresh();
    }
    public void Start()
    {
        Engine.IsVisible = Titles.IsVisible;
        if (preview)
        {
            Engine.Apply(new("preview1", "codex", "one", "UserPromptSubmit", "", "", "", "VibeCodingLight", "", false, 1));
            Engine.Apply(new("preview2", "opencode", "two", "SessionStart", "", "", "", "Web sitesi", "", false, 2, Title: "Web sitesi geliştirme"));
        }
        else
        {
            Titles.Initialize();
            if(Installer.IsInstalled && Preferences.StartAtLogin && StartupService.IsEnabled(Preferences) && Preferences.StartupRegistration!=StartupService.Content(Storage.Exe)) {
                try{StartupService.Change(Preferences,true);}catch(Exception e){Notice="Giriş kaydı onarılamadı: "+e.Message;}
            }
            try { Storage.Save("sessions.json", Array.Empty<Session>()); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notice = "Oturum geçmişi temizlenemedi: " + e.Message; }
            // Only an installed, previously opted-in integration may be repaired automatically.
            if (Installer.IsInstalled) foreach (string family in Integrations.Families)
            {
                if (Preferences.OwnedCommands.GetValueOrDefault(family)?.Count > 0 && !Integrations.Installed(Preferences, family))
                {
                    try { Integrations.Change(Preferences, family, true); hookNoticeFamily = family; Notice = "Eksik veya eski hook kayıtları onarıldı. Güven onayını kontrol et; ilgili uygulamayı tamamen kapatıp yeniden aç ve yeni görev başlat."; }
                    catch (Exception e) { Notice = HarnessCatalog.Label(family) + ": " + e.Message; }
                }
            }
            _ = DesktopEnvironment.Platform.Listen(text => Dispatcher.UIThread.Post(() => Receive(text)), stop.Token);
            approvalWatcher = new CodexApprovalWatcher();
            approvalTask = approvalWatcher.Run((session, outcome) => Dispatcher.UIThread.Post(() => ResolveAutomaticPermission(session, outcome)), stop.Token,
                started: session => Dispatcher.UIThread.Post(() => { permissionGate.ReviewStarted(session, DateTime.UtcNow); Tick(); }));
            worker = Task.Run(async () =>
            {
                using var device = new Device();
                while (!stop.IsCancellationRequested)
                {
                    (int R, int G, int B) rgb; OutputMode mode; int level; string selected, id;
                    lock (gate) { rgb = desired; mode = outputMode; level = brightness; selected = port; id = deviceId; }
                    await device.TickAsync(selected, id, rgb, level, mode, stop.Token); DeviceState = device.State;
                    if (DeviceState is { Connected: true, DeviceId.Length: > 0 } verified && verified.DeviceId != id)
                        Dispatcher.UIThread.Post(() => {
                            if (Exiting || Preferences.Port != selected || Preferences.DeviceId != id) return;
                            Preferences.DeviceId = verified.DeviceId; Save();
                        });
                    try { await Task.Delay(50, stop.Token); } catch (OperationCanceledException) { break; }
                }
            });
        }
        Diagnostics.Record("Startup", "app", "", DiagnosticJournal.Capture(Engine), DiagnosticJournal.Capture(Engine), Preferences.Brightness, false, DeviceState.Connected);
        testExpiryTimer.Tick += (_, _) => { testExpiryTimer.Stop(); lightPreview.Finish(); Tick(); };
        permissionGraceTimer.Tick += (_, _) => { permissionGraceTimer.Stop(); Tick(); };
        timer.Tick += (_, _) => Tick(); timer.Start(); Tick();
        if (!preview && Preferences.AutoCheckUpdates) _ = Window.CheckUpdates();
    }
    internal void ResolveAutomaticPermission(string session, string outcome)
    {
        if (Exiting) return;
        permissionGate.ReviewFinished(session); string key = "codex:" + session;
        var before = DiagnosticJournal.Capture(Engine, key);
        if (!Engine.ResolveAutomaticPermission(session)) return;
        Diagnostics.Record("PermissionAutoResolved", "codex", session, before, DiagnosticJournal.Capture(Engine, key), Preferences.Brightness, Testing, DeviceState.Connected, source: outcome);
        Persist(); Tick();
    }
    internal void Receive(string text)
    {
        if (Exiting) return;
        if (text == "SHOW") { Show(); return; }
        if (text == "EXIT") { Exit(); return; }
        try
        {
            var e = HookWire.Read(text); if (e is null) return;
            if (e.Harness == "codex" && e.MonitorRoot.Length > 0) approvalWatcher?.ObserveRoot(e.MonitorRoot);
            string key = e.Harness + ":" + e.SessionId;
            var before = DiagnosticJournal.Capture(Engine, key);
            bool pending = Engine.Sessions.Any(s => s.Key == key && s.Pending.Any(p => p.StartsWith("permit:", StringComparison.Ordinal)));
            Engine.Apply(e);
            if (permissionGate.Observe(e, pending, Engine, DateTime.UtcNow)) { permissionGraceTimer.Stop(); permissionGraceTimer.Start(); }
            if (Engine.Sessions.Any(s => s.Key == key)) observed.Observe(e.Harness);
            Diagnostics.Record(e.EventName, e.Harness, e.SessionId, before, DiagnosticJournal.Capture(Engine, key), Preferences.Brightness, Testing, DeviceState.Connected, e.ToolId, e.Correlation, e.TurnId, e.Source);
            if (hookNoticeFamily == e.Harness) { hookNoticeFamily = null; Notice = "Hook bağlantısı doğrulandı: oturum olayı alındı."; }
            Persist(); Tick();
        }
        catch (JsonException) { }
    }
    public void Tick()
    {
        if (Exiting) return;
        if (!preview && !CheckingConnections && DateTime.UtcNow >= nextHealth) _ = CheckConnections();
        if (!preview && Engine.PruneIdleSessions(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Length > 0) Persist();
        if (!preview) Titles.Refresh(Engine.Sessions);
        foreach (var s in Engine.Sessions.Where(s => s.IsOpen).ToArray()) if (!ProcessTree.Alive(s.OwnerPid, s.OwnerStart))
        {
            var before = DiagnosticJournal.Capture(Engine, s.Key); Engine.OwnerEnded(s.Key);
            Diagnostics.Record("OwnerEnded", s.Harness, s.Key[(s.Key.IndexOf(':') + 1)..], before, DiagnosticJournal.Capture(Engine, s.Key), Preferences.Brightness, Testing, DeviceState.Connected);
        }
        bool testing = lightPreview.Update(Engine.Aggregate);
        if (!testing) testExpiryTimer.Stop();
        if (wasTesting && !testing) Record("ColorTestEnded"); wasTesting = testing;
        var effect = testing ? LightEffect.None : Engine.Effect;
        if (effect == LightEffect.IdleBreathing) { idleStarted ??= DateTime.UtcNow; if (StateEngine.IdleBreathingExpired(Preferences.IdleAutoOffEnabled, Preferences.IdleAutoOffMinutes, DateTime.UtcNow - idleStarted.Value)) effect = LightEffect.None; } else idleStarted = null;
        PermissionReviewHeld = !testing && permissionGate.Hold(Engine, Mode, DateTime.UtcNow);
        lock (gate)
        {
            brightness = Preferences.Brightness; port = Preferences.Port; deviceId = Preferences.DeviceId;
            if (!PermissionReviewHeld)
            {
                Mode = testing ? OutputMode.Test : effect == LightEffect.IdleBreathing ? OutputMode.Blue : effect == LightEffect.WorkingAndIdle ? OutputMode.Yellow : Engine.Aggregate == Activity.NeedsInput ? OutputMode.Red : Engine.Aggregate == Activity.Working ? OutputMode.Green : OutputMode.Off;
                desired = testing ? ScaleTestColor(lightPreview.Color, brightness) : StateEngine.Color(Engine.Aggregate, brightness);
                outputMode = Mode;
            }
        }
        Window.Refresh(); Changed?.Invoke();
    }
    public Task CheckConnections(bool manual = false)
    {
        if (preview || Exiting) return Task.CompletedTask;
        if (connectionCheck is { IsCompleted: false } active) return manual ? CheckAfter(active) : active;
        return connectionCheck = CheckConnectionsCore();
    }
    private async Task CheckAfter(Task active) { await active; await CheckConnections(); }
    private async Task CheckConnectionsCore()
    {
        if (Exiting) return;
        CheckingConnections = true;
        var enabled = Integrations.Families.ToDictionary(f => f, f => Preferences.OwnedCommands.GetValueOrDefault(f)?.Count > 0);
        Window.Refresh();
        try
        {
            var checks = await Task.Run(() => Integrations.Families.Select(f => ConnectionHealth.Check(f, enabled[f])).ToArray());
            if (Exiting) return;
            foreach (var check in checks) { observed.Checked(check.Family, check.Fingerprint); Connections[check.Family] = check; }
            LastConnectionCheck = DateTime.Now;
        }
        finally { CheckingConnections = false; nextHealth = DateTime.UtcNow.AddHours(6); if (!Exiting) Window.Refresh(); }
    }
    private void Record(string name) { var state = DiagnosticJournal.Capture(Engine); Diagnostics.Record(name, "app", "", state, state, Preferences.Brightness, Testing, DeviceState.Connected); }
    public void Forget(string key) { var before = DiagnosticJournal.Capture(Engine, key); Engine.Remove(key); Diagnostics.Record("SessionRemoved", key.Split(':')[0], key[(key.IndexOf(':') + 1)..], before, DiagnosticJournal.Capture(Engine, key), Preferences.Brightness, Testing, DeviceState.Connected); Persist(); Tick(); }
    public void ToggleLight(string key) { Engine.ToggleLight(key); Persist(); Tick(); }
    public void Test(string color) { var rgb = color switch { "red" => (255, 0, 0), "blue" => (0, 0, 255), _ => (0, 255, 0) }; TestColor(rgb.Item1, rgb.Item2, rgb.Item3); }
    internal static (int R, int G, int B) ScaleTestColor((int R, int G, int B) rgb, int brightness) => ((int)Math.Round(rgb.R * brightness / 100.0), (int)Math.Round(rgb.G * brightness / 100.0), (int)Math.Round(rgb.B * brightness / 100.0));
    public void PreviewBrightness() { bool starting = !Testing; if (lightPreview.StartBrightness(Engine.Aggregate)) { if (starting) Record("ColorTestStarted"); RestartTestExpiry(); } Tick(); }
    public void TestColor(int r, int g, int b) { bool starting = !Testing; lightPreview.StartColor(r, g, b); if (starting) Record("ColorTestStarted"); RestartTestExpiry(); Tick(); }
    private void RestartTestExpiry() { testExpiryTimer.Stop(); testExpiryTimer.Start(); }
    public void ResetIdleCountdown() { idleStarted = null; }
    public void Save() { try { if (!preview) Storage.Save("settings.json", Preferences); Record("SettingsChanged"); } catch (Exception e) { Notice = "Ayar kaydedilemedi: " + e.Message; } Tick(); }
    public void Persist() { try { if (!preview) Storage.Save("sessions.json", Engine.Sessions); } catch (Exception e) { Notice = "Oturum kaydı yazılamadı: " + e.Message; } }
    public void Change(string family, bool install)
    {
        if (preview) return;
        try { Integrations.Change(Preferences, family, install); hookNoticeFamily = install ? family : null; Notice = HarnessCatalog.Label(family) + (install ? " bağlantısı etkinleştirildi. Güven onayını kontrol et; uygulamayı tamamen kapatıp yeniden aç ve yeni oturumda mesaj gönder." : " hook'ları kaldırıldı; yedek saklandı. İlgili uygulamayı tamamen kapatıp yeniden aç."); }
        catch (Exception e) { Notice = e.Message; }
        observed.Reset(family); _ = CheckConnections(true); Tick();
    }
    public void Show() { Window.Show(); Window.WindowState = Avalonia.Controls.WindowState.Normal; Window.Activate(); Tick(); }
    public async void Exit() { if (Exiting) return; Exiting = true; Persist(); timer.Stop(); testExpiryTimer.Stop(); permissionGraceTimer.Stop(); stop.Cancel(); Record("Exit"); await Task.WhenAny(Diagnostics.Complete(), Task.Delay(2000)); if (approvalTask is not null) await Task.WhenAny(approvalTask, Task.Delay(2000)); if (worker is not null) await Task.WhenAny(worker, Task.Delay(2000)); if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime app) app.Shutdown(); }
    public void Dispose() { stop.Cancel(); timer.Stop(); testExpiryTimer.Stop(); permissionGraceTimer.Stop(); _ = Diagnostics.Complete(); }
}
