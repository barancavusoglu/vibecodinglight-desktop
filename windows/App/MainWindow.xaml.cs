using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiLight.Core;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;

namespace AiLight.App;
public partial class MainWindow : Window {
    private sealed record DurationChoice(int Minutes, string Label) { public override string ToString() => Label; }
    private readonly Runtime runtime;
    private bool ready;
    private static readonly string[] BrightnessNames = ["En düşük", "Düşük", "Orta", "Yüksek", "Çok yüksek", "En yüksek"];
    private string sessionVisualKey = "";
    public string? HookNoticeFamily { get; set; }
    private void DismissNotice_Click(object sender, RoutedEventArgs e) { Notice.Text = ""; }
    public MainWindow(Runtime runtime) {
        this.runtime = runtime; InitializeComponent();
        BuildThemeChoices();
        ApplyTheme();
        SourceInitialized += (_, _) => ApplyTheme();
        VersionLabel.Text = $"VibeCodingLight {typeof(MainWindow).Assembly.GetName().Version!.ToString(3)} · Windows beta";
        for (int level = 0; level < SettingSteps.BrightnessLevels.Length; level++) {
            var bar = new Border { Width = 28, Height = 12 + level * 6, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Bottom };
            var content = new Grid { Height = 44 }; content.Children.Add(bar);
            var button = new Button { Content = content, Tag = level, Height = 60, Padding = new Thickness(4), Margin = new Thickness(0,0,4,0), Background = Brushes.Transparent, ToolTip = BrightnessNames[level] };
            button.Click += Brightness_Click;
            BrightnessChoices.Children.Add(button);
        }
        RefreshBrightnessChoices();
        IdleAutoOff.IsChecked = runtime.Preferences.IdleAutoOffEnabled;
        IdleAutoOffMinutes.ItemsSource = SettingSteps.IdleMinutes.Select(minutes => new DurationChoice(minutes, minutes < 60 ? minutes + " dakika" : minutes / 60 + " saat")).ToArray();
        IdleAutoOffMinutes.SelectedValue = runtime.Preferences.IdleAutoOffMinutes;
        UpdateIdleAutoOffUi();
        Autostart.IsChecked = runtime.Preferences.StartAtLogin;
        AutoUpdates.IsChecked = runtime.Preferences.AutoCheckUpdates;
        InstallAppButton.Visibility = string.Equals(System.IO.Path.GetDirectoryName(Storage.Exe), Storage.InstallDir, StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;
        RefreshPorts(); ready = true; Refresh();
        Closing += (_, e) => { if (!runtime.Exiting) { e.Cancel = true; Hide(); } };
    }
    public void Refresh() {
        var mode = runtime.CurrentLightMode;
        bool reviewing = runtime.PermissionReviewHeld;
        bool Checking(Session s) => LightPresentation.CheckingPermission(s, reviewing);
        Activity Shown(Session s) => LightPresentation.SessionActivity(s, reviewing, mode);
        var allSessions = runtime.Engine.Sessions.ToList();
        var sessions = allSessions.Where(s => runtime.Engine.IsVisible(s) && s.IsOpen && s.Activity != Activity.Unknown)
            .OrderBy(s => s.ExcludedFromLight ? 3 : Shown(s) == Activity.NeedsInput ? 0 : Shown(s) == Activity.Working ? 1 : 2).ThenByDescending(s => s.LastEvent).ToList();
        int working = sessions.Count(s => !s.ExcludedFromLight && Shown(s) == Activity.Working);
        int waiting = sessions.Count(s => !s.ExcludedFromLight && Shown(s) == Activity.NeedsInput);
        int idle = sessions.Count(s => !s.ExcludedFromLight && Shown(s) == Activity.Idle);
        Headline.Text = mode == OutputMode.Red ? "Müdahale gerekiyor"
            : mode is OutputMode.Green or OutputMode.Yellow ? "İşler yolunda" : "Şimdilik her şey sakin";
        Summary.Text = runtime.PreviewingBrightness ? "Parlaklık önizlemesi · 3 saniye"
            : runtime.Testing ? "Cihaz testi · gerçek durum birazdan geri gelir"
            : reviewing ? "İzin kontrol ediliyor · ışık durumu korunuyor"
            : waiting > 0 ? $"{working} çalışıyor · {waiting} müdahale gerekiyor"
            : $"{working} çalışıyor · {idle} yeni görev bekliyor";
        var eye = mode switch {
            OutputMode.Red => "#E78070", OutputMode.Yellow => "#E2C85B",
            OutputMode.Green => "#A8D88C", OutputMode.Blue => "#5498E8",
            _ => "#819588"
        };
        EyeLeft.Fill = EyeRight.Fill = Brush(eye);
        // Do not recreate cards every timer tick: it interrupts scrolling and keyboard focus.
        string visualKey = System.Text.Json.JsonSerializer.Serialize(sessions.Select(s => new { s.Key, Title = runtime.Titles.Get(s), s.Harness, s.Project, s.Reason, Activity = Shown(s), Checking = Checking(s), s.ExcludedFromLight, runtime.Preferences.Theme }));
        if (visualKey != sessionVisualKey) {
        sessionVisualKey = visualKey;
        SessionsList.ItemsSource = sessions.Select(s => new { s.Key, Title = runtime.Titles.Get(s),
            Frame = s.ExcludedFromLight ? (SolidColorBrush)Resources["Muted"] : Brush(Shown(s) == Activity.NeedsInput ? "#D96653" : Shown(s) == Activity.Working ? "#49A76D" : Shown(s) == Activity.Idle ? "#4D8CD4" : "#819588"),
            GlowColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Shown(s) == Activity.NeedsInput ? "#E36E5C" : Shown(s) == Activity.Working ? "#44BE78" : Shown(s) == Activity.Idle ? "#5498E8" : "#819588"),
            GlowOpacity = s.ExcludedFromLight ? 0.0 : Themes.IsDark(runtime.Preferences.Theme) ? 0.42 : 0.25,
            StripeVisibility = s.ExcludedFromLight ? Visibility.Visible : Visibility.Collapsed,
            EyePath = s.ExcludedFromLight ? "M2,12 Q12,1 22,12 Q12,23 2,12 M3,3 L21,21" : "M2,12 Q12,1 22,12 Q12,23 2,12 M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12",
            EyeTip = s.ExcludedFromLight ? "Işığa dahil et" : "Işıktan hariç tut (soru ve izinler dahil)",
            HarnessLabel = HarnessCatalog.Label(s.Harness) + (s.Project.Length > 0 ? " · " + s.Project : ""),
            Detail = s.ExcludedFromLight ? "Işığa dahil değil · " + s.Reason : Checking(s) ? "İzin kontrol ediliyor" : s.Reason,
            Color = s.ExcludedFromLight ? (SolidColorBrush)Resources["Muted"] : Shown(s) is Activity.NeedsInput or Activity.Working ? Brush(Themes.SessionColor(runtime.Preferences.Theme, Shown(s) == Activity.NeedsInput)) : (SolidColorBrush)Resources["Muted"] }).ToList();
        }
        EmptyState.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var deviceState = runtime.DeviceState;
        DeviceText.Text = deviceState.Detail; DeviceDot.Fill = deviceState.Connected ? Brush("#6F9868") : Brush("#A9B0A9");
        DeviceConnectionTitle.Text = deviceState.Connected ? "Cihaz bağlı" : "Cihaz bağlı değil";
        DeviceConnectedCheck.Visibility = deviceState.Connected ? Visibility.Visible : Visibility.Collapsed;
        DeviceConnectionType.Text = deviceState.Transport switch {
            DeviceTransport.Usb => "Bağlantı türü: USB",
            DeviceTransport.Bluetooth => "Bağlantı türü: Bluetooth",
            _ => "Bağlantı yok"
        };
        DeviceConnectionDetail.Text = deviceState.Detail;
        RefreshBrightnessChoices();
        foreach (var family in HarnessCatalog.Families) {
            var (card, status) = family switch { "codex" => (CodexCard, CodexStatus), "claude" => (ClaudeCard, ClaudeStatus), "opencode" => (OpenCodeCard, OpenCodeStatus), "copilot" => (CopilotCard, CopilotStatus), _ => (AntigravityCard, AntigravityStatus) };
            card.Tag = ConnectionAppearance(family);
            var text = runtime.ConnectionStatus(family);
            if ((string)card.Tag == "disabled") text = "Etkin değil";
            status.Text = text;
        }
        HealthAlert.Text = runtime.HealthWarning;
        HealthBanner.Visibility = HealthAlert.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        HealthChecked.Text = runtime.LastConnectionCheck is DateTime checkedAt
            ? $"Son kontrol: {checkedAt:dd.MM HH:mm}" : "Son kontrol: —";
    }
    private string ConnectionAppearance(string family) {
        if (runtime.Connections.TryGetValue(family, out var check))
            return !check.Enabled ? "disabled" : check.Healthy ? "healthy" : "unhealthy";
        return runtime.Preferences.OwnedCommands.GetValueOrDefault(family)?.Count > 0 ? "checking" : "disabled";
    }
    private static SolidColorBrush Brush(string color) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    private void ApplyTheme() {
        Themes.Apply(this, runtime.Preferences.Theme);
        ThemeLabel.Text = "Seçili: " + Themes.Name(runtime.Preferences.Theme) + " · LED renklerini değiştirmez";
        foreach (Button button in ThemeChoices.Children) {
            bool selected = (string)button.Tag == Themes.Normalize(runtime.Preferences.Theme);
            button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, selected ? "Accent" : "Edge");
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetName(button, Themes.Name((string)button.Tag) + (selected ? " · seçili" : ""));
        }
        RefreshBrightnessChoices();
    }
    private void BuildThemeChoices() {
        foreach (string theme in Themes.All) {
            var palette = Themes.Palette(theme);
            var preview = new Grid { Height = 28, Margin = new Thickness(0,0,0,9) };
            foreach (var color in new[] { palette[0], palette[5], palette[8] }) {
                preview.ColumnDefinitions.Add(new ColumnDefinition());
                var swatch = new Border { Background = Brush(color), CornerRadius = new CornerRadius(4), Margin = new Thickness(2,0,2,0) };
                Grid.SetColumn(swatch, preview.ColumnDefinitions.Count - 1); preview.Children.Add(swatch);
            }
            var content = new StackPanel(); content.Children.Add(preview);
            var name = new TextBlock { Text = Themes.Name(theme), FontWeight = FontWeights.SemiBold };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); content.Children.Add(name);
            var detail = new TextBlock { Text = Themes.Description(theme), FontSize = 11, Margin = new Thickness(0,3,0,0), TextWrapping = TextWrapping.Wrap };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(detail);
            var button = new Button { Tag = theme, Content = content, Margin = new Thickness(3,0,3,7), Padding = new Thickness(9), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch };
            button.Click += Theme_Click; ThemeChoices.Children.Add(button);
        }
    }
    private void Theme_Click(object sender, RoutedEventArgs e) {
        runtime.Preferences.Theme = Themes.Normalize((string)((Button)sender).Tag);
        ApplyTheme(); Refresh(); runtime.SavePreferences();
    }
    private void Brightness_Click(object sender, RoutedEventArgs e) {
        if (!ready) return;
        runtime.Preferences.Brightness = SettingSteps.BrightnessPercent((int)((Button)sender).Tag);
        runtime.PreviewBrightness();
        runtime.SavePreferences();
    }
    private void RefreshBrightnessChoices() {
        int selected = SettingSteps.BrightnessLevel(runtime.Preferences.Brightness);
        foreach (Button button in BrightnessChoices.Children) {
            int level = (int)button.Tag;
            var bar = (Border)((Grid)button.Content).Children[0];
            if (level <= selected) bar.SetResourceReference(Border.BackgroundProperty, "Accent");
            else bar.Background = Brushes.Transparent;
            bar.SetResourceReference(Border.BorderBrushProperty, level <= selected ? "Accent" : "Muted");
            System.Windows.Automation.AutomationProperties.SetName(button, "Parlaklık · " + BrightnessNames[level] + (level == selected ? " · seçili" : ""));
        }
    }
    private void IdleAutoOff_Click(object sender, RoutedEventArgs e) {
        if (!ready) return;
        runtime.Preferences.IdleAutoOffEnabled = IdleAutoOff.IsChecked == true;
        UpdateIdleAutoOffUi(); runtime.ResetIdleAutoOffCountdown(); runtime.SavePreferences();
    }
    private void IdleAutoOffMinutes_Changed(object sender, SelectionChangedEventArgs e) {
        if (!ready || IdleAutoOffMinutes.SelectedValue is not int minutes) return;
        runtime.Preferences.IdleAutoOffMinutes = minutes;
        runtime.ResetIdleAutoOffCountdown(); runtime.SavePreferences();
    }
    private void UpdateIdleAutoOffUi() {
        IdleAutoOffMinutes.IsEnabled = IdleAutoOff.IsChecked == true;
    }
    private void RefreshPorts() {
        bool wasReady = ready; ready = false;
        PortSelector.ItemsSource = new[] { "Otomatik algıla" }.Concat(Device.Ports())
            .Concat(runtime.Preferences.Port.Length > 0 ? new[] { runtime.Preferences.Port } : []).Distinct().ToArray();
        PortSelector.SelectedItem = runtime.Preferences.Port.Length == 0 ? "Otomatik algıla" : runtime.Preferences.Port;
        if (PortSelector.SelectedIndex < 0) PortSelector.SelectedIndex = 0;
        ready = wasReady;
    }
    private void Port_Changed(object sender, SelectionChangedEventArgs e) {
        if (!ready) return;
        runtime.Preferences.Port = PortSelector.SelectedIndex <= 0 ? "" : PortSelector.SelectedItem?.ToString() ?? "";
        runtime.Preferences.DeviceId = ""; runtime.SavePreferences();
    }
    private void Scan_Click(object sender, RoutedEventArgs e) { RefreshPorts(); }
    private void Forget_Click(object sender, RoutedEventArgs e) { runtime.Forget((string)((Button)sender).Tag); }
    private void ToggleLight_Click(object sender, RoutedEventArgs e) { runtime.ToggleLight((string)((Button)sender).Tag); }
    private void HealthDetails_Click(object sender, RoutedEventArgs e) { MainTabs.SelectedIndex = 1; }
    private async void Verify_Click(object sender, RoutedEventArgs e) {
        await VerifyConnections();
    }
    public async Task VerifyConnections() {
        if (!VerifyButton.IsEnabled) return;
        VerifyButton.IsEnabled = false; VerifyButton.Content = "Kontrol ediliyor…";
        VerifyProgress.Visibility = Visibility.Visible;
        VerifyResult.Text = "Hook kayıtları inceleniyor…";
        VerifyResult.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Muted");
        try {
            await Task.WhenAll(runtime.CheckConnections(), Task.Delay(450));
            var checks = runtime.Connections.Values.Where(c => c.Enabled).ToArray();
            int problems = checks.Count(c => !c.Healthy);
            VerifyResult.Text = checks.Length == 0 ? "Kontrol tamamlandı · etkin entegrasyon yok. Aşağıdan bir araç etkinleştir."
                : problems > 0 ? $"Kontrol tamamlandı · {problems} bağlantı için onarım gerekiyor. Ayrıntılar aşağıda."
                : $"✓ Kontrol tamamlandı · {checks.Length} entegrasyonun kayıtları sağlıklı. Gerçek olay durumu aşağıda gösteriliyor.";
            VerifyResult.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, problems > 0 ? "Notice" : "Ink");
        } catch (Exception ex) { VerifyResult.Text = "Kontrol tamamlanamadı: " + ex.Message; }
        finally { VerifyButton.IsEnabled = true; VerifyButton.Content = "Yeniden doğrula"; VerifyProgress.Visibility = Visibility.Collapsed; }
    }
    private void InstallHook_Click(object sender, RoutedEventArgs e) => ChangeHook((string)((Button)sender).Tag, true);
    private void RemoveHook_Click(object sender, RoutedEventArgs e) => ChangeHook((string)((Button)sender).Tag, false);
    private void ChangeHook(string family, bool install) {
        try { Integrations.Change(runtime.Preferences, family, install); HookNoticeFamily = install ? family : null; Notice.Text = family == "opencode" ? (install ? "OpenCode eklentisi kuruldu. OpenCode’u yeniden başlatıp bir mesaj gönder." : "OpenCode eklentisi kaldırıldı (yedek saklandı). OpenCode’u yeniden başlat.") : install ? "Hook kurulu. Henüz olay alınmadı; harness güven onayını kontrol edip bir mesaj gönder." : "VibeCodingLight hook'ları kaldırıldı."; Refresh(); }
        catch (Exception ex) { Notice.Text = ex.Message; }
        runtime.ResetConnectionObservation(family);
        _ = runtime.CheckConnections();
    }
    private void ColorWheel_PreviewColorChanged(object? sender, ColorSelectedEventArgs e) {
        var color = System.Windows.Media.Color.FromRgb((byte)e.R, (byte)e.G, (byte)e.B);
        SelectedColorPreview.Background = new SolidColorBrush(color);
        SelectedColorLabel.Text = $"#{e.R:X2}{e.G:X2}{e.B:X2}\nRGB {e.R}, {e.G}, {e.B}";
        runtime.Test(e.R, e.G, e.B);
    }
    private void Autostart_Click(object sender, RoutedEventArgs e) {
        try { Storage.Autostart(Autostart.IsChecked == true); runtime.Preferences.StartAtLogin = Autostart.IsChecked == true; runtime.SavePreferences(); }
        catch (Exception ex) { Notice.Text = ex.Message; Autostart.IsChecked = runtime.Preferences.StartAtLogin; }
    }
    private ReleaseInfo? availableRelease;
    private bool updateBusy;
    private void AutoUpdates_Click(object sender, RoutedEventArgs e) {
        runtime.Preferences.AutoCheckUpdates = AutoUpdates.IsChecked == true;
        runtime.SavePreferences();
    }
    public async Task CheckUpdates() {
        if (updateBusy) return;
        updateBusy = true; UpdateButton.IsEnabled = false;
        try {
            UpdateStatus.Text = "Güncelleme kontrol ediliyor…";
            availableRelease = await UpdateService.Check();
            ShowUpdateResult(availableRelease);
        } catch (Exception ex) { availableRelease = null; UpdateButton.Content = "Güncellemeleri kontrol et"; UpdateStatus.Text = "Kontrol tamamlanamadı: " + ex.Message; }
        finally { updateBusy = false; UpdateButton.IsEnabled = true; }
    }
    internal void ShowUpdateResult(ReleaseInfo? release) {
        availableRelease = release;
        UpdateStatus.Text = release is null ? "Güncelsin." : $"{release.Version} sürümü hazır. İndirme sayfasını açıp yeni sürümü indirerek mevcut uygulamanın üzerine kurabilirsin. Ayarların korunur.";
        if (release is not null) Notice.Text = $"VibeCodingLight {release.Version} hazır · İndirme bağlantısı Ayarlar'da.";
        UpdateButton.Content = release is null ? "Güncellemeleri kontrol et" : "İndirme sayfasını aç";
    }
    private async void Update_Click(object sender, RoutedEventArgs e) {
        if (updateBusy) return;
        if (availableRelease is null) { await CheckUpdates(); return; }
        updateBusy = true; UpdateButton.IsEnabled = false;
        try {
            UpdateService.OpenDownloadPage();
            UpdateStatus.Text = "İndirme sayfası tarayıcıya gönderildi. Yeni sürümü indirip mevcut uygulamanın üzerine kurabilirsin. Ayarların korunur.";
        } catch (Exception ex) { UpdateStatus.Text = "İndirme sayfası açılamadı: " + ex.Message; }
        finally { updateBusy = false; UpdateButton.IsEnabled = true; }
    }
    private void InstallApp_Click(object sender, RoutedEventArgs e) {
        try { Installer.StartInstall(); } catch (Exception ex) { Notice.Text = ex.Message; }
    }
}
