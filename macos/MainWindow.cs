using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AiLight.Core;
namespace AiLight.Mac;
public sealed class MainWindow : Window {
    private readonly Runtime runtime;
    public TabControl Tabs { get; } = new();
    private readonly TextBlock headline = Text("Şimdilik her şey sakin", 21), summary = Text("", 12), device = Text("", 11), notice = Text("", 12);
    private readonly StackPanel sessions = new() { Spacing = 8 };
    private readonly Dictionary<string, TextBlock> statuses = [];
    private readonly TextBlock brightnessText = Text("", 12);
    internal TextBlock DeviceConnectionTitle { get; } = Text("Cihaz bağlı değil", 14);
    internal TextBlock DeviceConnectionType { get; } = Text("Bağlantı yok", 13);
    internal TextBlock DeviceConnectionDetail { get; } = Text("Cihaz aranıyor", 12);
    internal Avalonia.Controls.Shapes.Path DeviceConnectedCheck { get; } = new() {
        Data = Geometry.Parse("M 1,7 L 5,11 L 13,2"), Stroke = Brush("#6F9868"), StrokeThickness = 2.2,
        Width = 14, Height = 14, Margin = new Thickness(8,0,0,0), VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false
    };
    internal Border DeviceCard { get; private set; } = null!;
    private readonly DockPanel noticeRow = new() { IsVisible = false };
    private string sessionSnapshot = "";
    private readonly List<Border> cards = [];
    private readonly List<TextBlock> muted = [];
    private bool ready;
    public MainWindow(Runtime runtime) {
        this.runtime = runtime; Title = "VibeCodingLight"; Width = 440; Height = 720; MinWidth = 400; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(22), LastChildFill = true };
        var header = new StackPanel { Spacing = 5, Margin = new Thickness(0,0,0,16) };
        header.Children.Add(Text("VibeCodingLight", 17)); header.Children.Add(Muted("Masanın küçük yardımcısı · macOS beta"));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var heroContent = new StackPanel { Spacing = 8 }; heroContent.Children.Add(headline); heroContent.Children.Add(summary);
        var hero = Card(heroContent); hero.Margin = new Thickness(0,0,0,18); DockPanel.SetDock(hero,Dock.Top); root.Children.Add(hero);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0,12,0,0) };
        var dismiss = Action("Tamam", () => { runtime.Notice = ""; Refresh(); });
        DockPanel.SetDock(dismiss, Dock.Right); noticeRow.Children.Add(dismiss); noticeRow.Children.Add(notice);
        footer.Children.Add(noticeRow); footer.Children.Add(device); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        notice.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) noticeRow.IsVisible = !string.IsNullOrEmpty(notice.Text); };
        Tabs.Items.Add(new TabItem { Header = "Oturumlar", Content = SessionTab() });
        Tabs.Items.Add(new TabItem { Header = "Bağlantılar", Content = ConnectionsTab() });
        Tabs.Items.Add(new TabItem { Header = "Ayarlar", Content = SettingsTab() });
        root.Children.Add(Tabs); Content = root;
        Closing += (_, e) => { if (!runtime.Exiting && !Program.Preview) { e.Cancel = true; Hide(); } };
        ready = true; SetTheme(runtime.Preferences.Theme);
    }
    private Control SessionTab() {
        var panel = new DockPanel { Margin = new Thickness(0,14,0,0) };
        var brightness = new StackPanel { Spacing = 8 }; brightness.Children.Add(brightnessText);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = runtime.Preferences.Brightness, TickFrequency = 1, IsSnapToTickEnabled = true };
        slider.PropertyChanged += (_, e) => { if (ready && e.Property == Slider.ValueProperty) { runtime.Preferences.Brightness = (int)slider.Value; runtime.Save(); } };
        brightness.Children.Add(slider); brightness.Children.Add(Muted("Seçimin kaydedilir · %0 ışığı kapatır"));
        var card = Card(brightness); DockPanel.SetDock(card, Dock.Bottom); panel.Children.Add(card);
        panel.Children.Add(new ScrollViewer { Content = sessions, Margin = new Thickness(0,0,0,10) }); return panel;
    }
    private Control ConnectionsTab() {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(0,14,0,0) };
        panel.Children.Add(Action("Bağlantıları doğrula", () => { _ = runtime.CheckConnections(true); Refresh(); }));
        foreach (string family in Integrations.Families) {
            var content = new StackPanel { Spacing = 9 }; content.Children.Add(Text(Label(family),16));
            content.Children.Add(Muted(family == "antigravity" ? "Beta · çalışma/boşta ve ask_question soru bekleme takibi; izin bekleme henüz yok." : HarnessCatalog.IsPreview(family) ? HarnessCatalog.PreviewNote : family == "claude" ? "Code CLI + yerel masaüstü Code sekmesi" : family == "codex" ? "CLI + masaüstü · yerel hook olayları" : "Yerel OpenCode eklentisi"));
            var status = Text("Etkin değil",12); statuses[family] = status; content.Children.Add(status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(Action("Etkinleştir / Onar", () => runtime.Change(family, true)));
            buttons.Children.Add(Action("Kaldır", () => runtime.Change(family, false))); content.Children.Add(buttons); panel.Children.Add(Card(content));
        }
        panel.Children.Add(Muted("Önce uygulamayı Applications klasörüne kur. Codex hook güven onayını kullanıcı verir. Claude Chat/Cowork ve uzak sunucular kapsam dışı. Gerçek Mac doğrulaması bekleniyor."));
        return new ScrollViewer { Content = panel };
    }
    private Control SettingsTab() {
        var panel = new StackPanel { Spacing = 13, Margin = new Thickness(0,14,0,0) };
        var idleOff = new CheckBox { Content = "Boşta ışığını süre sonunda söndür", IsChecked = runtime.Preferences.IdleAutoOffEnabled };
        idleOff.IsCheckedChanged += (_, _) => { if (ready) { runtime.Preferences.IdleAutoOffEnabled = idleOff.IsChecked == true; runtime.Save(); } };
        panel.Children.Add(idleOff);
        var idleMinutes = new ComboBox { ItemsSource = SettingSteps.IdleMinutes.Select(m => m + " dakika").ToArray(), SelectedIndex = Array.IndexOf(SettingSteps.IdleMinutes, runtime.Preferences.IdleAutoOffMinutes) };
        idleMinutes.SelectionChanged += (_, _) => { if (ready && idleMinutes.SelectedIndex >= 0) { runtime.Preferences.IdleAutoOffMinutes = SettingSteps.IdleMinutes[idleMinutes.SelectedIndex]; runtime.Save(); } };
        panel.Children.Add(idleMinutes);
        panel.Children.Add(Text("GÖRÜNÜM",12));
        var themes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        themes.Children.Add(Action("Gece", () => { SetTheme("dark"); runtime.Save(); }));
        themes.Children.Add(Action("Açık", () => { SetTheme("light"); runtime.Save(); })); panel.Children.Add(themes);
        var devicePanel = new StackPanel { Spacing = 10 };
        var deviceHeading = Text("Cihaz", 16); deviceHeading.FontWeight = FontWeight.SemiBold;
        devicePanel.Children.Add(deviceHeading);
        var deviceRow = new StackPanel { Orientation = Orientation.Horizontal };
        DeviceConnectionTitle.FontWeight = FontWeight.SemiBold;
        deviceRow.Children.Add(DeviceConnectionTitle); deviceRow.Children.Add(DeviceConnectedCheck);
        devicePanel.Children.Add(deviceRow); devicePanel.Children.Add(DeviceConnectionType);
        muted.Add(DeviceConnectionDetail); devicePanel.Children.Add(DeviceConnectionDetail);
        devicePanel.Children.Add(Muted("USB öncelikli · USB yoksa Bluetooth"));
        var ports = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        void Scan() {
            bool wasReady = ready; ready = false;
            ports.ItemsSource = new[] { "Otomatik algıla" }.Concat(OperatingSystem.IsMacOS() ? Device.Ports() : Array.Empty<string>())
                .Concat(runtime.Preferences.Port.Length > 0 ? new[] { runtime.Preferences.Port } : []).Distinct().ToArray();
            ports.SelectedItem = runtime.Preferences.Port.Length == 0 ? "Otomatik algıla" : runtime.Preferences.Port;
            if (ports.SelectedIndex < 0) ports.SelectedIndex = 0;
            ready = wasReady;
        }
        Scan(); ports.SelectionChanged += (_, _) => { if (!ready) return; runtime.Preferences.Port = ports.SelectedIndex <= 0 ? "" : ports.SelectedItem?.ToString() ?? ""; runtime.Preferences.DeviceId = ""; runtime.Save(); };
        devicePanel.Children.Add(ports); devicePanel.Children.Add(Action("Cihazları yenile", Scan));
        devicePanel.Children.Add(Muted("Powerbank ile kullanırken Bluetooth üzerinden bağlanır."));
        var tests = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tests.Children.Add(Action("Kırmızı", () => runtime.Test("red"))); tests.Children.Add(Action("Yeşil", () => runtime.Test("green"))); tests.Children.Add(Action("Mavi", () => runtime.Test("blue"))); devicePanel.Children.Add(tests);
        devicePanel.Children.Add(Muted("Renk testi 3 saniye sürer. Seri Monitör kapalı olmalı."));
        DeviceCard = Card(devicePanel); panel.Children.Add(DeviceCard);
        panel.Children.Add(Text("VibeCodingLight 0.1.4 · macOS önizleme",14));
        panel.Children.Add(Text("www.vibecodinglight.com",13));
        panel.Children.Add(Muted("Web sitesi ve imzalı güncelleme kanalı henüz yayında değil. Bu önizlemede güncelleme yeni .app paketiyle yapılır."));
        panel.Children.Add(Muted("Otomatik başlatmak için macOS Sistem Ayarları → Genel → Giriş Öğeleri bölümüne uygulamayı ekleyebilirsin."));
        panel.Children.Add(Action("Uygulamadan çık", runtime.Exit)); return new ScrollViewer { Content = panel };
    }
    public void SetTheme(string theme) {
        runtime.Preferences.Theme = theme;
        if (Application.Current is { } app) app.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Background = Brush(theme == "dark" ? "#14171D" : "#F7F6F2"); Foreground = Brush(theme == "dark" ? "#EDF1F7" : "#25332C");
        foreach (var card in cards) card.Background = Brush(theme == "dark" ? "#202832" : "#FFFFFF");
        foreach (var text in muted) text.Foreground = Brush(theme == "dark" ? "#AAB5C5" : "#606F65");
        sessionSnapshot = "";
    }
    public void Refresh() {
        var all = runtime.Engine.Sessions.OrderBy(s => s.Activity == Activity.NeedsInput ? 0 : s.Activity == Activity.Working ? 1 : 2).ThenByDescending(s => s.LastEvent).ToArray();
        headline.Text = runtime.ShownActivity == Activity.NeedsInput ? "Müdahale gerekiyor" : runtime.ShownActivity == Activity.Working ? "İşler yolunda" : "Şimdilik her şey sakin";
        summary.Text = runtime.Summary; brightnessText.Text = $"Işık parlaklığı · %{runtime.Preferences.Brightness}";
        var deviceState = runtime.DeviceState;
        device.Text = deviceState.Detail; notice.Text = runtime.Notice;
        DeviceConnectionTitle.Text = deviceState.Connected ? "Cihaz bağlı" : "Cihaz bağlı değil";
        DeviceConnectedCheck.IsVisible = deviceState.Connected;
        DeviceConnectionType.Text = deviceState.Transport switch {
            DeviceTransport.Usb => "Bağlantı türü: USB",
            DeviceTransport.Bluetooth => "Bağlantı türü: Bluetooth",
            _ => "Bağlantı yok"
        };
        DeviceConnectionDetail.Text = deviceState.Detail;
        noticeRow.IsVisible = runtime.Notice.Length > 0;
        string snapshot = string.Join("|", all.Select(s => s.Key + s.Title + runtime.Shown(s) + s.Reason + s.ExcludedFromLight + runtime.PermissionReviewHeld));
        if (snapshot != sessionSnapshot) {
            sessionSnapshot = snapshot; sessions.Children.Clear();
            if (all.Length == 0) sessions.Children.Add(Text("İlk oturumunu bekliyorum. Bağlantılar sekmesinden entegrasyonları etkinleştir.",14));
            foreach (var s in all) {
                var content = new DockPanel(); var forget = Action("×", () => { runtime.Engine.Remove(s.Key); runtime.Persist(); runtime.Tick(); });
                DockPanel.SetDock(forget,Dock.Right); content.Children.Add(forget);
                var hide = Action(s.ExcludedFromLight ? "Dahil et" : "Hariç tut", () => { runtime.Engine.ToggleLight(s.Key); runtime.Persist(); runtime.Tick(); });
                DockPanel.SetDock(hide, Dock.Right); content.Children.Add(hide);
                var texts = new StackPanel { Spacing = 7, Margin = new Thickness(0,0,10,0) };
                texts.Children.Add(Text(s.Title.Length > 0 ? s.Title : s.Project + " · " + s.Key[^Math.Min(8,s.Key.Length)..],14));
                texts.Children.Add(Text(Label(s.Harness) + " · " + (s.ExcludedFromLight ? "Işığa dahil değil" : LightPresentation.CheckingPermission(s, runtime.PermissionReviewHeld) ? "İzin kontrol ediliyor" : s.Reason),12)); content.Children.Add(texts);
                sessions.Children.Add(new Border { Child = content, Background = Brush(runtime.Preferences.Theme == "dark" ? "#202832" : "#FFFFFF"), CornerRadius = new CornerRadius(12), Padding = new Thickness(13) });
            }
        }
        foreach (var (family, label) in statuses) label.Text = Program.Preview ? "Önizleme" : runtime.CheckingConnections ? "Kontrol ediliyor…" : !runtime.Connections.GetValueOrDefault(family) ? runtime.Preferences.OwnedCommands.GetValueOrDefault(family)?.Count > 0 ? "Onarım gerekiyor" : "Etkin değil" : all.Any(s => s.Harness == family && s.Activity != Activity.Unknown) ? "Gerçek oturum olayı alındı" : "Kurulu · ilk olay bekleniyor";
    }
    private Border Card(Control content) { var card = new Border { Child = content, CornerRadius = new CornerRadius(16), Padding = new Thickness(16) }; cards.Add(card); return card; }
    private TextBlock Muted(string value) { var text = Text(value,12); muted.Add(text); return text; }
    private static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static Button Action(string title, Action action) { var button = new Button { Content = title, Padding = new Thickness(12,8) }; button.Click += (_,_) => action(); return button; }
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static string Label(string family) => HarnessCatalog.Label(family);
}
