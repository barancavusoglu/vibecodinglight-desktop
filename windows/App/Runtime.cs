using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using AiLight.Core;
using Forms = System.Windows.Forms;

namespace AiLight.App;
public sealed class Runtime : IDisposable {
    public Preferences Preferences { get; } = Storage.Load();
    public StateEngine Engine { get; } = new();
    public SessionTitles Titles { get; } = new();
    public Diagnostics Diagnostics { get; }
    public MainWindow Window { get; private set; } = null!;
    public bool Exiting { get; private set; }
    public bool Testing => DateTime.UtcNow < testUntil;
    public bool PreviewingBrightness => Testing && brightnessPreview;
    internal (int R, int G, int B) PreviewColor => testColor;
    public DeviceConnectionState DeviceState { get; private set; } = new(DeviceTransport.None, "Cihaz aranıyor");
    public string DeviceStatus => DeviceState.Detail;
    public bool DeviceConnected => DeviceState.Connected;
    internal void PreviewDeviceState(DeviceConnectionState state) {
        if (!offline) throw new InvalidOperationException("Device preview requires offline mode");
        DeviceState = state; Window.Refresh();
    }
    private Forms.NotifyIcon? tray;
    private Icon? trayIcon;
    private readonly CancellationTokenSource cancel = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer testExpiryTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer permissionGraceTimer = new() { Interval = PermissionLightGate.Grace };
    private readonly PermissionLightGate permissionLightGate = new();
    private Task? deviceTask;
    private Task? approvalTask;
    private CodexApprovalWatcher? approvalWatcher;
    private DateTime testUntil;
    private (int R, int G, int B) testColor = (255, 0, 0);
    private bool brightnessPreview;
    private bool wasTesting;
    private readonly bool offline;
    private (int R, int G, int B) desired;
    private int effectBrightness;
    private string port = "", deviceId = "";
    private readonly object gate = new();
    private OutputMode? previous;
    private DateTime? idleBreathingStartedUtc;
    private OutputMode outputMode;
    public OutputMode CurrentLightMode => outputMode;
    public bool PermissionReviewHeld { get; private set; }
    internal void PreviewLightMode(OutputMode mode, bool held) {
        if (!offline) throw new InvalidOperationException("Light preview state is test-only.");
        outputMode = mode; PermissionReviewHeld = held; Window.Refresh();
    }
    private Task? connectionCheck;
    private DateTime nextConnectionCheck;
    public DateTime? LastConnectionCheck { get; private set; }
    public Dictionary<string, ConnectionCheck> Connections { get; } = [];
    private readonly HashSet<string> observedConnections = [];
    public bool HasObservedConnection(string family) => observedConnections.Contains(family);
    public string HealthWarning => string.Join(", ", Connections.Values.Where(c => c.Enabled && !c.Healthy).Select(c => HarnessCatalog.Label(c.Family))) is { Length: > 0 } names ? names + " · kayıtları kontrol et / onar. Değişiklikten sonra ilgili uygulamayı veya CLI oturumunu tamamen kapatıp yeniden açman gerekebilir." : "";
    public string ConnectionStatus(string family) {
        if (!Connections.TryGetValue(family, out var check)) return "Kontrol bekleniyor";
        if (!check.Healthy) return check.Detail;
        return check.Detail + (HasObservedConnection(family) ? " · gerçek olay alındı" : " · gerçek olay bekleniyor. Güven onayını kontrol et; gerekirse ilgili uygulamayı veya CLI oturumunu tamamen kapatıp yeniden aç ve yeni oturumda mesaj gönder.");
    }
    public void ResetConnectionObservation(string family) => observedConnections.Remove(family);
    public Task CheckConnections() {
        if (offline || Exiting) return Task.CompletedTask;
        return connectionCheck is { IsCompleted: false } ? connectionCheck : connectionCheck = CheckConnectionsCore();
    }
    private async Task CheckConnectionsCore() {
        var enabled = ConnectionHealth.Families.ToDictionary(f => f, f => Preferences.OwnedCommands.GetValueOrDefault(f)?.Count > 0);
        try {
            var checks = await Task.Run(() => ConnectionHealth.Families.Select(f => ConnectionHealth.Check(f, enabled[f])).ToArray());
            if (Exiting) return;
            foreach (var check in checks) {
                if (Connections.TryGetValue(check.Family, out var old) && old.Fingerprint != check.Fingerprint)
                    observedConnections.Remove(check.Family);
                Connections[check.Family] = check;
            }
            LastConnectionCheck = DateTime.Now;
            Window.Refresh();
        } finally { nextConnectionCheck = DateTime.UtcNow.AddHours(6); }
    }
    public Runtime(bool offline = false) {
        this.offline = offline;
        Diagnostics = new Diagnostics(!offline);
        Engine.IsVisible = Titles.IsVisible;
        if (!offline) Titles.Initialize();
    }
    public void Start(bool background) {
        // Session state is live-only. Persisted history cannot prove that an AI
        // is still active after an app/PC restart and caused false lights.
        if (!offline) try { Storage.Save("sessions.json", Array.Empty<Session>()); } catch { }
        Window = new MainWindow(this);
        var initial = AiLight.App.Diagnostics.Capture(Engine);
        Diagnostics.Record("Startup", "app", "", initial, initial, Preferences.Brightness, false, false);
        if (!offline && Path.GetDirectoryName(Storage.Exe)!.Equals(Storage.InstallDir, StringComparison.OrdinalIgnoreCase)
            && Preferences.OwnedCommands.GetValueOrDefault("opencode")?.Count > 0 && !OpenCodeIntegration.Installed()) {
            try { OpenCodeIntegration.Change(Preferences, true); Window.Notice.Text = "OpenCode eklentisi güncellendi; OpenCode'u yeniden başlat."; }
            catch (Exception ex) { Window.Notice.Text = "OpenCode: " + ex.Message; }
        }
        if (!offline && Path.GetDirectoryName(Storage.Exe)!.Equals(Storage.InstallDir, StringComparison.OrdinalIgnoreCase)) {
            foreach (var family in HarnessCatalog.Families.Where(f => f != "opencode")) {
                var owned = Preferences.OwnedCommands.GetValueOrDefault(family);
                if (owned?.Count > 0 && !Integrations.IsInstalled(Preferences, family)) {
                    try { Integrations.Change(Preferences, family, true); Window.HookNoticeFamily = family; Window.Notice.Text = (family == "codex" ? "Eksik veya eski hook kayıtları onarıldı. Codex'te yeni/değişen kayıtların güven onayını kontrol et." : HarnessCatalog.Label(family) + " hook kayıtları onarıldı.") + " Değişikliklerin uygulanması için ilgili uygulamayı veya CLI oturumunu tamamen kapatıp yeniden açman gerekebilir; ardından yeni oturumda mesaj gönder."; }
                    catch (Exception ex) { Window.Notice.Text = "Hook taşıma: " + ex.Message; }
                }
            }
        }
        tray = new Forms.NotifyIcon { Text = "VibeCodingLight", Visible = !offline };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Show(); };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("VibeCodingLight'yi aç", null, (_, _) => Show());
        menu.Items.Add("Çıkış", null, (_, _) => Exit());
        tray.ContextMenuStrip = menu;
        UpdateTray();
        if (!background) Window.Show();
        timer.Tick += (_, _) => Tick(); timer.Start();
        testExpiryTimer.Tick += (_, _) => { testExpiryTimer.Stop(); testUntil = DateTime.MinValue; Tick(); };
        permissionGraceTimer.Tick += (_, _) => { permissionGraceTimer.Stop(); Tick(); };
        if (offline) return;
        _ = CheckConnections();
        if (Preferences.AutoCheckUpdates) _ = Window.CheckUpdates();
        _ = Bridge.Listen(text => System.Windows.Application.Current.Dispatcher.Invoke(() => Receive(text)), cancel.Token);
        approvalWatcher = new CodexApprovalWatcher();
        approvalTask = approvalWatcher.Run((sessionId, outcome) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() => ResolveAutomaticPermission(sessionId, outcome)), cancel.Token,
            started: sessionId => System.Windows.Application.Current.Dispatcher.Invoke(() => {
                permissionLightGate.ReviewStarted(sessionId, DateTime.UtcNow); Tick();
            }));
        deviceTask = Task.Run(async () => {
            using var device = new Device();
            while (!cancel.IsCancellationRequested) {
                (int R, int G, int B) rgb; string chosen, id; int brightness; OutputMode mode;
                lock (gate) { rgb = desired; chosen = port; id = deviceId; brightness = effectBrightness; mode = outputMode; }
                await device.TickAsync(chosen, id, rgb, brightness, mode, cancel.Token);
                DeviceState = device.State;
                try { await Task.Delay(50, cancel.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }
    private void ResolveAutomaticPermission(string sessionId, string outcome) {
        permissionLightGate.ReviewFinished(sessionId);
        string key = "codex:" + sessionId;
        var before = AiLight.App.Diagnostics.Capture(Engine, key);
        bool changed = Engine.ResolveAutomaticPermission(sessionId);
        if (!changed) return;
        Diagnostics.Record("PermissionAutoResolved", "codex", sessionId, before,
            AiLight.App.Diagnostics.Capture(Engine, key), Preferences.Brightness, Testing, DeviceConnected, source: outcome);
        Persist(); Tick();
    }
    internal void Receive(string text) {
        if (text == "SHOW") { Show(); return; }
        if (text == "EXIT") { Exit(); return; }
        try { var e = JsonSerializer.Deserialize<HookEvent>(text); if (e is not null) {
            if (e.Harness == "codex" && e.MonitorRoot.Length > 0) approvalWatcher?.ObserveRoot(e.MonitorRoot);
            string key = e.Harness + ":" + e.SessionId;
            var before = AiLight.App.Diagnostics.Capture(Engine, key);
            bool alreadyPendingPermission = Engine.Sessions.Any(s => s.Key == key
                && s.Pending.Any(p => p.StartsWith("permit:", StringComparison.Ordinal)));
            Engine.Apply(e);
            if (permissionLightGate.Observe(e, alreadyPendingPermission, Engine, DateTime.UtcNow)) {
                permissionGraceTimer.Stop(); permissionGraceTimer.Start();
            }
            if (Engine.Sessions.Any(s => s.Key == key)) observedConnections.Add(e.Harness);
            Diagnostics.Record(e.EventName, e.Harness, e.SessionId, before, AiLight.App.Diagnostics.Capture(Engine, key), Preferences.Brightness, Testing, DeviceConnected, e.ToolId, e.Correlation, e.TurnId, e.Source);
            Persist();
            if (Window.HookNoticeFamily == e.Harness) {
                Window.Notice.Text = "Hook bağlantısı doğrulandı: oturum olayı alındı.";
                Window.HookNoticeFamily = null;
            }
            Tick();
        } }
        catch (JsonException) { }
    }
    private void Tick() {
        if (!offline && DateTime.UtcNow >= nextConnectionCheck) _ = CheckConnections();
        if (brightnessPreview && Engine.Aggregate == Activity.NeedsInput) {
            testExpiryTimer.Stop(); testUntil = DateTime.MinValue;
        }
        if (wasTesting && !Testing) {
            var state = AiLight.App.Diagnostics.Capture(Engine);
            Diagnostics.Record("ColorTestEnded", "app", "", state, state, Preferences.Brightness, false, DeviceConnected);
        }
        wasTesting = Testing;
        if (!offline) {
            var before = AiLight.App.Diagnostics.Capture(Engine);
            if (Engine.PruneIdleSessions(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Length > 0) {
                Diagnostics.Record("SessionExpired", "app", "", before, AiLight.App.Diagnostics.Capture(Engine),
                    Preferences.Brightness, Testing, DeviceConnected);
                Persist();
            }
        }
        if (!offline) Titles.Refresh(Engine.Sessions);
        foreach (var s in Engine.Sessions.ToArray())
            if (s.IsOpen && !ProcessTree.Alive(s.OwnerPid, s.OwnerStart)) {
                var before = AiLight.App.Diagnostics.Capture(Engine, s.Key); Engine.OwnerEnded(s.Key);
                Diagnostics.Record("OwnerEnded", s.Harness, s.Key[(s.Key.IndexOf(':') + 1)..], before, AiLight.App.Diagnostics.Capture(Engine, s.Key), Preferences.Brightness, Testing, DeviceConnected);
            }
        var rgb = StateEngine.Color(Engine.Aggregate, Preferences.Brightness);
        if (Testing) {
            double scale = Preferences.Brightness / 100.0;
            rgb = ((int)Math.Round(testColor.R * scale), (int)Math.Round(testColor.G * scale), (int)Math.Round(testColor.B * scale));
        }
        var engineEffect = Testing ? LightEffect.None : Engine.Effect;
        var outputEffect = engineEffect;
        if (engineEffect == LightEffect.IdleBreathing) {
            idleBreathingStartedUtc ??= DateTime.UtcNow;
            if (StateEngine.IdleBreathingExpired(Preferences.IdleAutoOffEnabled, Preferences.IdleAutoOffMinutes,
                DateTime.UtcNow - idleBreathingStartedUtc.Value)) outputEffect = LightEffect.None;
        } else idleBreathingStartedUtc = null;
        bool holdLight = permissionLightGate.Hold(Engine, outputMode, DateTime.UtcNow);
        lock (gate) {
            PermissionReviewHeld = holdLight;
            if (!holdLight) desired = rgb;
            port = Preferences.Port; deviceId = Preferences.DeviceId;
            effectBrightness = Preferences.Brightness;
            if (!holdLight) outputMode = Testing ? OutputMode.Test : outputEffect == LightEffect.IdleBreathing ? OutputMode.Blue
                    : outputEffect == LightEffect.WorkingAndIdle ? OutputMode.Yellow
                    : Engine.Aggregate == Activity.NeedsInput ? OutputMode.Red
                    : Engine.Aggregate == Activity.Working ? OutputMode.Green : OutputMode.Off;
        }
        UpdateTray(); if (Window.IsVisible) Window.Refresh();
    }
    private void UpdateTray() {
        if (tray is null) return;
        if (previous != outputMode) {
            var old = trayIcon; trayIcon = CreateIcon(outputMode); tray.Icon = trayIcon; old?.Dispose(); previous = outputMode;
        }
        tray.Text = PermissionReviewHeld ? "VibeCodingLight · İzin kontrol ediliyor"
            : $"VibeCodingLight · {Engine.Sessions.Count(s => Engine.Included(s) && s.Activity == Activity.Working)} çalışıyor · {Engine.Sessions.Count(s => Engine.Included(s) && s.Activity == Activity.NeedsInput)} bekliyor";
    }
    public void Show() { Window.Show(); Window.WindowState = System.Windows.WindowState.Normal; Window.Activate(); Window.Refresh(); }
    public void PreviewBrightness() {
        if (Engine.Aggregate == Activity.NeedsInput) { Tick(); return; }
        var color = PreviewingBrightness ? testColor
            : ColorWheel.HsvToRgb(185 + Random.Shared.NextDouble() * 110, 1, 1);
        StartTest(color.R, color.G, color.B, true);
    }
    public void Test(int red, int green, int blue) => StartTest(red, green, blue, false);
    private void StartTest(int red, int green, int blue, bool previewBrightness) {
        bool starting = !Testing;
        if (starting) {
            var state = AiLight.App.Diagnostics.Capture(Engine);
            Diagnostics.Record("ColorTestStarted", "app", "", state, state, Preferences.Brightness, true, DeviceConnected);
        }
        brightnessPreview = previewBrightness;
        testColor = (Math.Clamp(red, 0, 255), Math.Clamp(green, 0, 255), Math.Clamp(blue, 0, 255));
        testUntil = DateTime.UtcNow.AddSeconds(3);
        testExpiryTimer.Stop(); testExpiryTimer.Start(); Tick();
    }
    public void Forget(string key) {
        var before = AiLight.App.Diagnostics.Capture(Engine, key); Engine.Remove(key);
        Diagnostics.Record("SessionRemoved", key.Split(':')[0], key[(key.IndexOf(':') + 1)..], before, AiLight.App.Diagnostics.Capture(Engine, key), Preferences.Brightness, Testing, DeviceConnected);
        Persist(); Tick();
    }
    public void ToggleLight(string key) { Engine.ToggleLight(key); Persist(); Tick(); }
    public void SavePreferences() {
        try {
            if (!offline) Storage.Save("settings.json", Preferences);
            var state = AiLight.App.Diagnostics.Capture(Engine);
            Diagnostics.Record("SettingsChanged", "app", "", state, state, Preferences.Brightness, Testing, DeviceConnected);
            Tick();
        }
        catch (Exception ex) { Window.Notice.Text = "Ayar kaydedilemedi: " + ex.Message; }
    }
    public void ResetIdleAutoOffCountdown() { idleBreathingStartedUtc = null; Tick(); }
    public void Persist() {
        try { if (!offline) Storage.Save("sessions.json", Engine.Sessions); }
        catch (Exception ex) { Window.Notice.Text = "Oturum kaydı yazılamadı: " + ex.Message; }
    }
    public async void Exit() {
        if (Exiting) return;
        Exiting = true; Persist(); timer.Stop(); testExpiryTimer.Stop(); permissionGraceTimer.Stop(); cancel.Cancel();
        var state = AiLight.App.Diagnostics.Capture(Engine);
        Diagnostics.Record("Exit", "app", "", state, state, Preferences.Brightness, false, DeviceConnected);
        await Task.WhenAny(Diagnostics.Complete(), Task.Delay(2000));
        if (approvalTask is not null) await Task.WhenAny(approvalTask, Task.Delay(2000));
        if (deviceTask is not null) await Task.WhenAny(deviceTask, Task.Delay(2000));
        Dispose(); System.Windows.Application.Current.Shutdown();
    }
    public void Dispose() { cancel.Cancel(); timer.Stop(); testExpiryTimer.Stop(); permissionGraceTimer.Stop(); tray?.Dispose(); trayIcon?.Dispose(); }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    private static Icon CreateIcon(OutputMode state) {
        using var bitmap = new Bitmap(32, 32); using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var tile = new SolidBrush(Color.FromArgb(40, 47, 37));
        using var body = new SolidBrush(Color.FromArgb(245, 243, 236));
        using var face = new SolidBrush(Color.FromArgb(38, 51, 40));
        using var eye = new SolidBrush(state switch {
            OutputMode.Red => Color.Tomato,
            OutputMode.Green => Color.FromArgb(181, 230, 157),
            OutputMode.Yellow => Color.Gold,
            OutputMode.Blue => Color.CornflowerBlue,
            _ => Color.Gray
        });
        g.FillRoundedRectangle(tile, new RectangleF(1, 1, 30, 30), 8.5f);
        using (var antenna = new Pen(body, 2) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            g.DrawLine(antenna, 16, 5.5f, 16, 9);
        g.FillEllipse(eye, 14.5f, 3.5f, 3, 3);
        g.FillRoundedRectangle(body, new RectangleF(4, 9, 24, 18), 7.5f);
        g.FillRoundedRectangle(face, new RectangleF(6.5f, 11.5f, 19, 13), 5);
        g.FillRoundedRectangle(eye, new RectangleF(10.5f, 15.5f, 4, 5.5f), 2);
        g.FillRoundedRectangle(eye, new RectangleF(17.5f, 15.5f, 4, 5.5f), 2);
        IntPtr handle = bitmap.GetHicon();
        try { using var icon = Icon.FromHandle(handle); return (Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
}
internal static class DrawingExtensions {
    public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF r, float radius) {
        using var path = new System.Drawing.Drawing2D.GraphicsPath(); float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); g.FillPath(brush, path);
    }
}
