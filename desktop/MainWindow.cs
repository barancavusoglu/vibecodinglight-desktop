using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using AiLight.Core;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
namespace AiLight.Desktop;

public sealed class MainWindow : Window
{
    private readonly Runtime runtime;
    public TabControl Tabs { get; } = new();
    private readonly TextBlock headline = Text("Şimdilik her şey sakin", 19), summary = Hint(""), device = Hint("", 11), notice = Text("", 12);
    private readonly AdaptiveCardPanel sessions = new();
    private readonly List<AdaptiveCardPanel> adaptivePanels = [];
    private readonly Grid sessionArea = new();
    private readonly StackPanel empty = new() { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
    private readonly Dictionary<string, TextBlock> statuses = [];
    private readonly Dictionary<string, StatusCard> connectionCards = [];
    private readonly Dictionary<string, Button> themeButtons = [];
    private readonly List<(Button Button, Border Bar)> brightnessButtons = [];
    private readonly Dictionary<string, (Button Hide, Button Forget)> sessionActions = [];
    private readonly List<Ellipse> eyes = [];
    private readonly TextBlock themeLabel = Hint(""), healthChecked = Hint(""), healthResult = Hint(""), healthAlert = Hint("");
    private readonly Border healthBanner;
    private readonly Button verifyButton;
    private readonly ProgressBar verifyProgress = new() { IsIndeterminate = true, Height = 3, IsVisible = false };
    private readonly Grid noticeRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), IsVisible = false };
    private readonly Ellipse deviceDot = new() { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly StatusCard hero;
    private string sessionSnapshot = "\0";
    private bool ready, verifying, updateBusy;
    private Action? refreshDeviceChoices;
    private readonly TextBlock updateStatus = Hint("Yeni sürüm olup olmadığını kontrol edebilirsin.");
    private Button updateButton = null!;
    private ReleaseInfo? availableRelease;
    private ScrollViewer settingsScroll = null!;
    private StatusCard appearanceCard = null!, applicationCard = null!;
    internal TextBlock DeviceConnectionTitle { get; } = Section("Cihaz bağlı değil", 14);
    internal TextBlock DeviceConnectionType { get; } = Text("Bağlantı yok", 13);
    internal TextBlock DeviceConnectionDetail { get; } = Hint("Cihaz aranıyor");
    internal ShapePath DeviceConnectedCheck { get; } = new() { Data = Geometry.Parse("M1,7 L5,11 L13,2"), Stroke = Brush("#6F9868"), StrokeThickness = 2.2, Width = 14, Height = 14, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    internal Border DeviceCard { get; private set; } = null!;
    internal Expander ColorTestExpander { get; private set; } = null!;
    internal ColorWheel TestColorWheel { get; private set; } = null!;

    public MainWindow(Runtime runtime)
    {
        this.runtime = runtime;
        Title = "VibeCodingLight"; Width = 460; Height = 760; MinWidth = 420; MinHeight = 600; FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Styles.Add(new StyleInclude(new Uri("avares://VibeCodingLight.Desktop/")) { Source = new Uri("avares://VibeCodingLight.Desktop/Styles.axaml") });
        var root = new DockPanel { Margin = new Thickness(24, 22, 24, 18), MaxWidth = 1640 };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var badge = new Border { Child = Hint("BETA", 10), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center };
        badge.Classes.Add("badge"); DockPanel.SetDock(badge, Dock.Right); header.Children.Add(badge);
        var brand = new StackPanel { Spacing = 6 }; brand.Children.Add(Section("VibeCodingLight", 20)); brand.Children.Add(Hint("Kodun çalışırken, ışığın anlatsın.")); header.Children.Add(brand);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        var healthContent = new DockPanel(); var review = Action("İncele", () => Tabs.SelectedIndex = 1); DockPanel.SetDock(review, Dock.Right); healthContent.Children.Add(review);
        var healthText = new StackPanel { Spacing = 5 }; healthText.Children.Add(Section("Bağlantı kontrolü gerekli", 13)); healthText.Children.Add(healthAlert); healthContent.Children.Add(healthText);
        healthBanner = new Border { Child = healthContent, Padding = new Thickness(12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), IsVisible = false }; healthBanner.Classes.Add("hero"); footer.Children.Add(healthBanner);
        notice.Margin = new Thickness(0,0,10,0);notice.VerticalAlignment=VerticalAlignment.Center;
        noticeRow.Children.Add(notice);var dismiss=Action("Tamam",()=>{runtime.Notice="";Refresh();});Grid.SetColumn(dismiss,1);noticeRow.Children.Add(dismiss);footer.Children.Add(noticeRow);
        var deviceRow = new DockPanel();
        var deviceLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 }; deviceLeft.Children.Add(deviceDot); deviceLeft.Children.Add(device); deviceRow.Children.Add(deviceLeft); footer.Children.Add(deviceRow);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var heroContent = new DockPanel(); var robot = RobotMark(); DockPanel.SetDock(robot, Dock.Left); heroContent.Children.Add(robot);
        var heroText = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center }; headline.FontWeight = FontWeight.SemiBold; heroText.Children.Add(headline); heroText.Children.Add(summary); heroContent.Children.Add(heroText);
        hero = Card(heroContent); hero.Classes.Add("hero"); hero.Padding = new Thickness(20); hero.MaxWidth = 560; hero.Margin = new Thickness(0, 0, 0, 24);
        verifyButton = Action("Bağlantıları doğrula", () => { _ = VerifyConnections(); }); verifyButton.Classes.Add("primary");
        AddTab("Oturumlar", SessionTab()); AddTab("Bağlantılar", ConnectionsTab()); AddTab("Ayarlar", SettingsTab());
        root.Children.Add(Tabs); Content = root;
        Closing += (_, e) => { if (!runtime.Exiting && !Program.Preview) { e.Cancel = true; Hide(); } };
        ready = true; SetTheme(runtime.Preferences.Theme);
    }
    private void AddTab(string title, Control content) { var tab = new TabItem { Header = title, Content = content }; AutomationProperties.SetName(tab, title); Tabs.Items.Add(tab); }
    private AdaptiveCardPanel Adaptive() { var panel = new AdaptiveCardPanel(); adaptivePanels.Add(panel); return panel; }
    private Control SessionTab()
    {
        var panel = new DockPanel(); DockPanel.SetDock(hero, Dock.Top); panel.Children.Add(hero);
        var title = Section("Canlı oturumlar"); title.Margin = new Thickness(2, 0, 0, 12); DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
        adaptivePanels.Add(sessions); sessions.Margin = new Thickness(0, 0, 10, 0);
        sessionArea.Children.Add(new ScrollViewer { Content = sessions, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var emptyTitle = Section("Birlikte çalışmaya hazırız.", 16); emptyTitle.TextAlignment = TextAlignment.Center; empty.Children.Add(emptyTitle);
        var emptyHint = Hint("Bağlantılar'dan kullandığın araçları etkinleştir. Yeni oturumların burada görünecek."); emptyHint.TextAlignment = TextAlignment.Center; empty.Children.Add(emptyHint);
        sessionArea.Children.Add(empty); panel.Children.Add(sessionArea); return panel;
    }
    private Control ConnectionsTab()
    {
        var panel = Page("Araçlarınla bağlantı kur.", "Kayıtlar ve gerçek olay akışı ayrı ayrı doğrulanır.");
        var health = new StackPanel { Spacing = 12 }; health.Children.Add(Section("Bağlantı sağlığı")); health.Children.Add(verifyButton); health.Children.Add(verifyProgress); health.Children.Add(healthResult);
        health.Children.Add(Hint("Sarı: gerçek olay bekleniyor. Yeşil: gerçek olay alındı. Etkinleştirme veya onarım sonrası ilgili uygulamayı ya da CLI oturumunu tamamen kapatıp yeniden açman gerekebilir; ardından yeni oturumda mesaj gönder.")); health.Children.Add(healthChecked);
        var healthCard = Card(health); healthCard.MaxWidth = 560; healthCard.Margin = new Thickness(8, 8, 10, 14); panel.Children.Add(healthCard);
        var cards = Adaptive();
        cards.LayoutUpdated += (_,_) => {
            if(connectionCards.TryGetValue("codex",out var first) && first.Bounds.Width>0 && (!double.IsFinite(healthCard.Width)||Math.Abs(healthCard.Width-first.Bounds.Width)>0.5))healthCard.Width=first.Bounds.Width;
        };
        foreach (string family in Integrations.Families)
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(Section(Label(family) + (HarnessCatalog.IsPreview(family) ? " · Beta" : "")));
            content.Children.Add(Hint(family switch { "codex" => "CLI ve masaüstü · yerel oturumlar", "claude" => "CLI ve masaüstü Code sekmesi", "opencode" => "Yerel eklenti", "copilot" => "CLI / yerel VS Code ajanı · sürüm doğrulaması gerekli", _ => "App / IDE / CLI · yerel lifecycle hook'ları" }));
            var status = Text("Etkin değil", 13); statuses[family] = status; content.Children.Add(status);
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            var enable = Action("Etkinleştir / Onar", () => runtime.Change(family, true)); enable.Margin = new Thickness(0, 0, 8, 0); buttons.Children.Add(enable);
            var remove = Action("Kaldır", () => runtime.Change(family, false)); remove.Classes.Add("quiet"); buttons.Children.Add(remove); content.Children.Add(buttons);
            content.Children.Add(Hint(family switch
            {
                "codex" => "Codex'te hook güven onayını tamamla. Gerekirse Codex uygulamasını veya CLI oturumunu tamamen kapatıp yeniden aç; ardından yeni görev başlat.",
                "claude" => "Etkinleştirdikten sonra gerekirse Claude uygulamasını veya CLI oturumunu tamamen kapatıp yeniden aç ve yeni oturum başlat. Chat, Cowork ve uzak oturumlar desteklenmiyor.",
                "opencode" => "Kurulum veya kaldırma sonrası OpenCode'u yeniden başlat. Uzak sunucular desteklenmiyor.",
                "copilot" => "Çalışma/boşta takibi. İzin, soru ve arka plan alt görev takibi henüz desteklenmiyor. Gerekirse VS Code'u veya Copilot CLI oturumunu tamamen kapatıp yeniden aç ve yeni görev başlat.",
                _ => "Çalışma/boşta ve ask_question soru bekleme takibi. İzin penceresi beklemesi izlenmez. Bitiş için fullyIdle sinyali gerekir. Uygulamayı veya CLI oturumunu yeniden aç ve yeni görev başlat."
            }));
            var card = Card(content); card.Margin = new Thickness(8, 8, 10, 14); connectionCards[family] = card; cards.Children.Add(card);
        }
        panel.Children.Add(cards); return Scroll(panel);
    }
    private Control SettingsTab()
    {
        var panel = Page("Tam sana göre.", "Değişiklikler otomatik kaydedilir."); var cards = Adaptive();
        var light = new StackPanel { Spacing = 12 }; light.Children.Add(Section("Işık")); light.Children.Add(Text("Parlaklık", 13));
        var choices = new UniformGrid { Rows = 1, Columns = 6 };
        string[] levels = ["En düşük", "Düşük", "Orta", "Yüksek", "Çok yüksek", "En yüksek"];
        for (int i = 0; i < 6; i++)
        {
            int level = i; var bar = new Border { Height = 12 + i * 6, Width = 28, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Bottom };
            var holder = new Grid { Height = 44 }; holder.Children.Add(bar);
            var choice = Action("", () => { if (!ready) return; runtime.Preferences.Brightness = SettingSteps.BrightnessPercent(level); runtime.PreviewBrightness(); runtime.Save(); }); choice.Content = holder; choice.Height = 60; choice.HorizontalAlignment = HorizontalAlignment.Stretch; choice.Classes.Add("brightness"); choice.Padding = new Thickness(4); choice.Margin = new Thickness(0, 0, 4, 0); ToolTip.SetTip(choice, $"{levels[i]} · %{SettingSteps.BrightnessPercent(i)}"); brightnessButtons.Add((choice, bar)); choices.Children.Add(choice);
        }
        light.Children.Add(choices); light.Children.Add(Divider());
        var idle = new CheckBox { Content = "Boşta kalınca ışığı söndür", IsChecked = runtime.Preferences.IdleAutoOffEnabled };
        var minutes = new ComboBox { ItemsSource = SettingSteps.IdleMinutes.Select(SettingSteps.DurationLabel).ToArray(), SelectedIndex = Array.IndexOf(SettingSteps.IdleMinutes, runtime.Preferences.IdleAutoOffMinutes), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = runtime.Preferences.IdleAutoOffEnabled };
        AutomationProperties.SetName(minutes, "Boşta ışığı söndürme süresi");
        idle.IsCheckedChanged += (_, _) => { if (!ready) return; runtime.Preferences.IdleAutoOffEnabled = idle.IsChecked == true; minutes.IsEnabled = idle.IsChecked == true; runtime.ResetIdleCountdown(); runtime.Save(); };
        minutes.SelectionChanged += (_, _) => { if (!ready || minutes.SelectedIndex < 0) return; runtime.Preferences.IdleAutoOffMinutes = SettingSteps.IdleMinutes[minutes.SelectedIndex]; runtime.ResetIdleCountdown(); runtime.Save(); };
        light.Children.Add(idle); light.Children.Add(Text("Bekleme süresi", 13)); light.Children.Add(minutes); light.Children.Add(Hint("Mavi nefes bu süre sonunda kapanır. Yeni çalışma başladığında ışık geri gelir.")); cards.Children.Add(Card(light));
        var appearance = new StackPanel { Spacing = 12 }; appearance.Children.Add(Section("Görünüm"));
        var themes = new UniformGrid { Columns = 2 };
        foreach (string theme in DesktopThemes.All)
        {
            var palette = DesktopThemes.Palette(theme); var content = new StackPanel { Spacing = 5 };
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(0, 0, 0, 4) };
            foreach (string color in new[] { palette[0], palette[5], palette[8] }) swatches.Children.Add(new Border { Width = 18, Height = 28, Background = Brush(color), CornerRadius = new CornerRadius(4) });
            content.Children.Add(swatches); content.Children.Add(Section(DesktopThemes.Name(theme), 13)); content.Children.Add(Hint(DesktopThemes.Description(theme), 11));
            var choice = Action("", () => { SetTheme(theme); runtime.Save(); }); choice.Content = content; choice.HorizontalAlignment = HorizontalAlignment.Stretch; choice.HorizontalContentAlignment = HorizontalAlignment.Stretch; choice.Padding = new Thickness(9); choice.Margin = new Thickness(3, 0, 3, 7); choice.Classes.Add("theme"); themeButtons[theme] = choice; themes.Children.Add(choice);
        }
        appearance.Children.Add(themes); appearance.Children.Add(themeLabel); appearanceCard = Card(appearance); cards.Children.Add(appearanceCard);
        var devicePanel = new StackPanel { Spacing = 12 }; devicePanel.Children.Add(Section("Cihaz"));
        var deviceRow = new StackPanel { Orientation = Orientation.Horizontal }; deviceRow.Children.Add(DeviceConnectionTitle); deviceRow.Children.Add(DeviceConnectedCheck); devicePanel.Children.Add(deviceRow); devicePanel.Children.Add(DeviceConnectionType); devicePanel.Children.Add(DeviceConnectionDetail); devicePanel.Children.Add(Hint("USB öncelikli · USB yoksa Bluetooth"));
        var ports = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(ports, "USB veya Bluetooth cihazı");
        void Scan() {
            if (ports.IsDropDownOpen) return;
            var choices = DevicePortChoice.Create(Program.Preview ? [] : Device.Ports(), runtime.Preferences.Port, DesktopEnvironment.Platform.IsMacOS, runtime.DeviceState.Address);
            var selection = choices.First(c => DevicePortChoice.Matches(c.Address, runtime.Preferences.Port, DesktopEnvironment.Platform.IsMacOS));
            if (ports.ItemsSource is DevicePortChoice[] current && current.SequenceEqual(choices) && Equals(ports.SelectedItem, selection)) return;
            bool previous = ready; ready = false; ports.ItemsSource = choices; ports.SelectedItem = selection; ready = previous;
        }
        refreshDeviceChoices = Scan;
        Scan(); ports.SelectionChanged += (_, _) => { if (!ready || ports.SelectedItem is not DevicePortChoice choice) return; runtime.Preferences.Port = choice.Address; runtime.Preferences.DeviceId = ""; runtime.Save(); };
        devicePanel.Children.Add(ports); devicePanel.Children.Add(Action("Cihazları yenile", Scan)); devicePanel.Children.Add(Hint("Powerbank ile kullanırken Bluetooth üzerinden bağlanır.")); devicePanel.Children.Add(Divider());
        TestColorWheel = new ColorWheel(); var testDetails = new StackPanel { Spacing = 10, Margin = new Thickness(12, 4, 0, 0) }; var selected = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Background = Brush("#FF0000"), HorizontalAlignment = HorizontalAlignment.Left }; var selectedLabel = Text("#FF0000", 12); testDetails.Children.Add(selected); testDetails.Children.Add(selectedLabel); testDetails.Children.Add(Hint("Basılı tutup sürükle."));
        TestColorWheel.ColorSelected += (r, g, b) => { selected.Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b)); selectedLabel.Text = $"#{r:X2}{g:X2}{b:X2}\nRGB {r}, {g}, {b}"; runtime.TestColor(r, g, b); };
        var wheelRow = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), Margin = new Thickness(0, 14, 0, 0) }; wheelRow.Children.Add(TestColorWheel); Grid.SetColumn(testDetails, 1); wheelRow.Children.Add(testDetails);
        var testPanel = new StackPanel { Spacing = 12 }; testPanel.Children.Add(wheelRow); testPanel.Children.Add(Hint("Son seçimden 3 saniye sonra normal duruma döner."));
        ColorTestExpander = new Expander { Header = "Renk testi", Content = testPanel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }; devicePanel.Children.Add(ColorTestExpander); DeviceCard = Card(devicePanel); cards.Children.Add(DeviceCard);
        var app = new StackPanel { Spacing = 12 }; app.Children.Add(Section("Uygulama"));
        var autostart = new CheckBox { Content = "Bilgisayar açılınca başlat", IsChecked = !Program.Preview && StartupService.IsEnabled(runtime.Preferences) };
        autostart.IsCheckedChanged += (_, _) =>
        {
            if (!ready) return;
            try { if (!Program.Preview) StartupService.Change(runtime.Preferences, autostart.IsChecked == true); else runtime.Preferences.StartAtLogin = autostart.IsChecked == true; runtime.Save(); }
            catch (Exception e) { runtime.Notice = e.Message; ready = false; autostart.IsChecked = runtime.Preferences.StartAtLogin; ready = true; runtime.Tick(); }
        };
        app.Children.Add(autostart); app.Children.Add(Hint("Oturum açıldığında arka planda başlatılır.")); app.Children.Add(Divider());
        var autoUpdates = new CheckBox { Content = "Açılışta yeni sürümü kontrol et", IsChecked = runtime.Preferences.AutoCheckUpdates };
        autoUpdates.IsCheckedChanged += (_, _) => { if (!ready) return; runtime.Preferences.AutoCheckUpdates = autoUpdates.IsChecked == true; runtime.Save(); };
        app.Children.Add(autoUpdates); app.Children.Add(Hint("Yeni sürüm varsa haber verir. Yeni paketi indirip kurulumu kendin yapabilirsin; ayarların korunur."));
        updateButton = Action("Güncellemeleri kontrol et", () => { _ = UpdateClicked(); }); app.Children.Add(updateButton); app.Children.Add(updateStatus); app.Children.Add(Divider());
        app.Children.Add(Section("VibeCodingLight hakkında", 13)); app.Children.Add(Text($"VibeCodingLight {typeof(MainWindow).Assembly.GetName().Version!.ToString(3)} · {DesktopEnvironment.Platform.Name} beta", 13)); app.Children.Add(Hint("www.vibecodinglight.com"));
        var install = Action("Bilgisayara kur", () => { try { if (!Program.Preview) Installer.StartInstall(); } catch (Exception e) { runtime.Notice = e.Message; runtime.Tick(); } }); install.IsVisible = !Installer.IsInstalled; app.Children.Add(install);
        var uninstall=Action("Uygulamayı kaldır",()=>{_=ConfirmUninstall();});uninstall.IsVisible=Installer.IsInstalled;app.Children.Add(uninstall);
        app.Children.Add(Action("Uygulamadan çık", runtime.Exit)); applicationCard = Card(app); cards.Children.Add(applicationCard);
        panel.Children.Add(cards); settingsScroll = Scroll(panel); return settingsScroll;
    }
    public void SetTheme(string theme)
    {
        theme = DesktopThemes.Normalize(theme); runtime.Preferences.Theme = theme;
        if (Application.Current is { } app) app.RequestedThemeVariant = DesktopThemes.IsDark(theme) ? ThemeVariant.Dark : ThemeVariant.Light;
        string[] palette = DesktopThemes.Palette(theme), keys = ["Page", "Ink", "Control", "Muted", "Hero", "Card", "Notice", "Edge", "Accent"];
        for (int i = 0; i < keys.Length; i++) Resources[keys[i]] = Brush(palette[i]);
        Background = Brush(palette[0]); Foreground = Brush(palette[1]); themeLabel.Text = "Seçili: " + DesktopThemes.Name(theme) + " · LED renklerini değiştirmez";
        foreach (var (id, choice) in themeButtons) { choice.Classes.Set("selected", id == theme); AutomationProperties.SetName(choice, DesktopThemes.Name(id) + (id == theme ? " · seçili" : "")); }
        sessionSnapshot = "\0";
    }
    public void Refresh()
    {
        refreshDeviceChoices?.Invoke();
        var all = runtime.Engine.Sessions.Where(s => runtime.Engine.IsVisible(s) && s.IsOpen && s.Activity != Activity.Unknown).OrderBy(s => s.ExcludedFromLight ? 3 : runtime.Shown(s) == Activity.NeedsInput ? 0 : runtime.Shown(s) == Activity.Working ? 1 : 2).ThenByDescending(s => s.LastEvent).ToArray();
        headline.Text = runtime.ShownActivity == Activity.NeedsInput ? "Müdahale gerekiyor" : runtime.ShownActivity == Activity.Working ? "İşler yolunda" : "Şimdilik her şey sakin";
        int working = all.Count(s => !s.ExcludedFromLight && runtime.Shown(s) == Activity.Working), waiting = all.Count(s => !s.ExcludedFromLight && runtime.Shown(s) == Activity.NeedsInput), idle = all.Count(s => !s.ExcludedFromLight && runtime.Shown(s) == Activity.Idle);
        summary.Text = runtime.PreviewingBrightness ? "Parlaklık önizlemesi · 3 saniye" : runtime.Testing ? "Cihaz testi · gerçek durum birazdan geri gelir" : runtime.PermissionReviewHeld ? "İzin kontrol ediliyor · ışık durumu korunuyor" : waiting > 0 ? $"{working} çalışıyor · {waiting} müdahale gerekiyor" : $"{working} çalışıyor · {idle} yeni görev bekliyor";
        string eye = runtime.Mode switch { OutputMode.Red => "#E78070", OutputMode.Green => "#A8D88C", OutputMode.Yellow => "#E2C85B", OutputMode.Blue => "#5498E8", _ => "#819588" }; foreach (var shape in eyes) shape.Fill = Brush(eye);
        device.Text = runtime.DeviceState.Summary; deviceDot.Fill = Brush(runtime.DeviceState.Connected ? "#6F9868" : "#A9B0A9"); notice.Text = runtime.Notice; noticeRow.IsVisible = runtime.Notice.Length > 0;
        DeviceConnectionTitle.Text = runtime.DeviceState.Connected ? "Cihaz bağlı" : "Cihaz bağlı değil"; DeviceConnectedCheck.IsVisible = runtime.DeviceState.Connected;
        DeviceConnectionType.Text = runtime.DeviceState.Transport switch { DeviceTransport.Usb => "Bağlantı türü: USB", DeviceTransport.Bluetooth => "Bağlantı türü: Bluetooth", _ => "Bağlantı yok" }; DeviceConnectionDetail.Text = runtime.DeviceState.Detail;
        string snapshot = System.Text.Json.JsonSerializer.Serialize(all.Select(s => new { s.Key, Title = runtime.Titles.Get(s), s.Project, s.Reason, s.ExcludedFromLight, Activity = runtime.Shown(s), runtime.PermissionReviewHeld, Theme = runtime.Preferences.Theme }));
        if (snapshot != sessionSnapshot) { sessionSnapshot = snapshot; sessions.Children.Clear(); sessionActions.Clear(); empty.IsVisible = all.Length == 0; foreach (var s in all) sessions.Children.Add(SessionCard(s)); }
        int selected = SettingSteps.BrightnessLevel(runtime.Preferences.Brightness);
        for (int i = 0; i < brightnessButtons.Count; i++) { var (button, bar) = brightnessButtons[i]; bar.Background = i <= selected ? Brush(DesktopThemes.Palette(runtime.Preferences.Theme)[8]) : Brushes.Transparent; bar.BorderBrush = Brush(DesktopThemes.Palette(runtime.Preferences.Theme)[i <= selected ? 8 : 3]); AutomationProperties.SetName(button, $"Parlaklık · %{SettingSteps.BrightnessPercent(i)}" + (i == selected ? " · seçili" : "")); }
        bool preview = Program.Preview;
        foreach (var (family, label) in statuses)
        {
            string state = preview ? Program.Arguments.Contains("--showcase") ? family switch { "codex" => "healthy", "claude" => "pending", "opencode" => "unhealthy", _ => "disabled" } : "disabled" : runtime.ConnectionAppearance(family);
            label.Text = preview ? state switch { "healthy" => "Gerçek oturum olayı alındı", "pending" => "Kurulu · ilk olay bekleniyor", "unhealthy" => "Onarım gerekiyor", _ => "Etkin değil" } : runtime.ConnectionStatus(family);
            ApplyState(connectionCards[family], state);
        }
        verifyButton.IsEnabled = !runtime.CheckingConnections && !verifying; verifyProgress.IsVisible = runtime.CheckingConnections || verifying;
        if (!verifying && string.IsNullOrEmpty(healthResult.Text)) healthResult.Text = "Bağlantıları ve kayıtları kontrol edebilirsin.";
        healthChecked.Text = runtime.LastConnectionCheck is DateTime at ? $"Son kontrol: {at:dd.MM HH:mm}" : "Son kontrol: —";
        healthBanner.IsVisible = runtime.HealthWarning.Length > 0; healthAlert.Text = runtime.HealthWarning;
    }
    public async Task VerifyConnections()
    {
        if (verifying) return; verifying = true; verifyButton.Content = "Kontrol ediliyor…"; healthResult.Text = "Hook kayıtları inceleniyor…"; Refresh();
        try
        {
            await Task.WhenAll(runtime.CheckConnections(true), Task.Delay(450));
            var checks = runtime.Connections.Values.Where(c => c.Enabled).ToArray(); int problems = checks.Count(c => !c.Healthy);
            healthResult.Text = checks.Length == 0 ? "Kontrol tamamlandı · etkin entegrasyon yok. Aşağıdan bir araç etkinleştir." : problems > 0 ? $"Kontrol tamamlandı · {problems} bağlantı için onarım gerekiyor. Ayrıntılar aşağıda." : $"✓ Kontrol tamamlandı · {checks.Length} entegrasyonun kayıtları sağlıklı. Gerçek olay durumu aşağıda gösteriliyor.";
        }
        catch (Exception e) { healthResult.Text = "Kontrol tamamlanamadı: " + e.Message; }
        finally { verifying = false; verifyButton.Content = "Yeniden doğrula"; Refresh(); }
    }
    public async Task CheckUpdates()
    {
        if (updateBusy) return;
        if (Program.Preview) { updateStatus.Text = "Önizleme · ağ bağlantısı kullanılmıyor."; return; }
        updateBusy = true; updateButton.IsEnabled = false; updateButton.Classes.Remove("update-available"); updateStatus.Text = "Güncelleme kontrol ediliyor…";
        try { ShowUpdateResult(await UpdateService.Check()); }
        catch (Exception e) { availableRelease = null; updateButton.Content = "Güncellemeleri kontrol et"; updateStatus.Text = "Kontrol tamamlanamadı: " + e.Message; }
        finally { updateBusy = false; updateButton.IsEnabled = true; }
    }
    internal void ShowUpdateResult(ReleaseInfo? release)
    {
        availableRelease = release; updateButton.Classes.Set("update-available", release is not null); updateButton.Content = release is null ? "Güncellemeleri kontrol et" : "İndirme sayfasını aç";
        updateStatus.Text = release is null ? "VibeCodingLight'ın en güncel sürümünü kullanıyorsunuz." : $"{release.Version} sürümü hazır. Yeni sürümü indirip mevcut uygulamanın üzerine kurabilirsin. Ayarların korunur.";
        if (release is not null) { runtime.Notice = $"VibeCodingLight {release.Version} hazır · İndirme bağlantısı Ayarlar'da."; Refresh(); }
    }
    private async Task UpdateClicked()
    {
        if (updateBusy) return; if (availableRelease is null) { await CheckUpdates(); return; }
        if (Program.Preview) return;
        try { UpdateService.OpenDownloadPage(); updateStatus.Text = "İndirme sayfası tarayıcıya gönderildi. Ayarların korunur."; }
        catch (Exception e) { updateStatus.Text = "İndirme sayfası açılamadı: " + e.Message; }
    }

    private StatusCard SessionCard(Session s)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; var texts = new StackPanel { Spacing = 7, Margin = new Thickness(0, 0, 10, 0) };
        var title = Section(runtime.Titles.Get(s), 13); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(title, title.Text); texts.Children.Add(title);
        texts.Children.Add(Hint(Label(s.Harness) + (s.Project.Length > 0 ? " · " + s.Project : "")));
        var detail = Text(s.ExcludedFromLight ? "Işığa dahil değil · " + s.Reason : LightPresentation.CheckingPermission(s, runtime.PermissionReviewHeld) ? "İzin kontrol ediliyor" : s.Reason, 12);
        detail.Foreground = Brush(s.ExcludedFromLight || runtime.Shown(s) == Activity.Idle ? DesktopThemes.Palette(runtime.Preferences.Theme)[3] : DesktopThemes.SessionColor(runtime.Preferences.Theme, runtime.Shown(s) == Activity.NeedsInput)); texts.Children.Add(detail); content.Children.Add(texts);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var hide = Action("", () => { runtime.ToggleLight(s.Key); }); hide.Classes.Add("icon"); hide.Padding = new Thickness(7); hide.Content = new ShapePath { Data = Geometry.Parse(s.ExcludedFromLight ? "M2,12 Q12,1 22,12 Q12,23 2,12 M3,3 L21,21" : "M2,12 Q12,1 22,12 Q12,23 2,12 M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12"), Width = 19, Height = 19, Stretch = Stretch.Uniform, Stroke = Brush(DesktopThemes.Palette(runtime.Preferences.Theme)[3]), StrokeThickness = 1.7 }; string eyeTip = s.ExcludedFromLight ? "Işığa dahil et" : "Işıktan hariç tut (soru ve izinler dahil)"; ToolTip.SetTip(hide, eyeTip); AutomationProperties.SetName(hide, eyeTip); actions.Children.Add(hide);
        var forget = Action("×", () => { runtime.Forget(s.Key); }); forget.Classes.Add("icon"); forget.Padding = new Thickness(7); forget.FontSize = 18; ToolTip.SetTip(forget, "Bu kaydı listeden kaldır"); AutomationProperties.SetName(forget, "Oturum kaydını kaldır"); actions.Children.Add(forget); sessionActions[s.Key] = (hide, forget); Grid.SetColumn(actions, 1); content.Children.Add(actions);
        var card = Card(content); card.Padding = new Thickness(16); card.Margin = new Thickness(8, 8, 10, 12); ApplyState(card, s.ExcludedFromLight ? "disabled" : runtime.Shown(s) switch { Activity.NeedsInput => "unhealthy", Activity.Working => "healthy", _ => "idle" }); return card;
    }
    private void ApplyState(StatusCard card, string state) { string key = state + ":" + runtime.Preferences.Theme; if (card.Tag as string == key) return; card.Tag = key; string hex = state switch { "healthy" => "#49A76D", "pending" => "#C69C37", "unhealthy" => "#D96653", "idle" => "#4D8CD4", _ => DesktopThemes.Palette(runtime.Preferences.Theme)[3] }; card.BorderBrush = Brush(hex); card.BorderThickness = new Thickness(1.5); card.SetStripe(state == "disabled", Brush(DesktopThemes.Palette(runtime.Preferences.Theme)[5]), Color.Parse(hex)); card.BoxShadow = state == "disabled" ? new BoxShadows() : new BoxShadows(new BoxShadow { Color = Color.FromArgb(DesktopThemes.IsDark(runtime.Preferences.Theme) ? (byte)95 : (byte)55, Color.Parse(hex).R, Color.Parse(hex).G, Color.Parse(hex).B), Blur = 14 }); }
    private async Task ConfirmUninstall() {
        if(Program.Preview)return;
        var confirm=new Window{Title="VibeCodingLight'ı kaldır",Width=420,Height=220,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var content=new StackPanel{Margin=new Thickness(20),Spacing=16};content.Children.Add(Text("VibeCodingLight hook'ları ve kendi giriş kaydı kaldırılacak. Uygulama Çöp'e taşınacak; ayarların ve tanı verilerin korunacak.",13));
        var choices=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};var cancel=Action("Vazgeç",()=>confirm.Close(false));var yes=Action("Kaldır",()=>confirm.Close(true));choices.Children.Add(cancel);choices.Children.Add(yes);content.Children.Add(choices);confirm.Content=content;
        if(!await confirm.ShowDialog<bool>(this))return;
        try{Installer.Uninstall(runtime.Preferences);runtime.Exit();}catch(Exception e){runtime.Notice="Kaldırma tamamlanamadı: "+e.Message;runtime.Tick();}
    }
    internal async Task VerifyConnectionUi()
    {
        if (!Program.Preview) throw new InvalidOperationException("UI checks require offline preview");
        await VerifyConnections();
        if (!verifyButton.IsEnabled || verifyProgress.IsVisible || verifyButton.Content?.ToString() != "Yeniden doğrula") throw new Exception("Connection verification did not finish cleanly");
        Console.WriteLine("PASS Verification button, progress and result complete correctly");
    }
    internal void VerifySessionUi()
    {
        if (!Program.Preview) throw new InvalidOperationException("UI checks require offline preview");
        runtime.Engine.Apply(new("ui-question", "claude", "question", "PermissionRequest", "", "request", "Bash", "Demo", "", false, 10)); runtime.Tick();
        if (runtime.Mode != OutputMode.Red) throw new Exception("Question did not preempt working sessions");
        sessionActions["claude:question"].Hide.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if (runtime.Engine.Aggregate == Activity.NeedsInput || !runtime.Engine.Sessions.Single(s => s.Key == "claude:question").ExcludedFromLight) throw new Exception("Eye action did not exclude a pending question");
        sessionActions["claude:question"].Hide.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if (runtime.Mode != OutputMode.Red) throw new Exception("Including a pending question did not restore red");
        sessionActions["claude:question"].Forget.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if (runtime.Engine.Sessions.Any(s => s.Key == "claude:question") || runtime.Engine.Aggregate == Activity.NeedsInput) throw new Exception("Remove action did not release its question");
        Console.WriteLine("PASS Session eye/include/remove handlers preserve aggregate question priority");
    }
    internal void VerifyUpdateUi()
    {
        if (!Program.Preview) throw new InvalidOperationException("UI checks require offline preview");
        ShowUpdateResult(new ReleaseInfo("0.1.99"));
        if (!updateButton.Classes.Contains("update-available") || updateButton.Content?.ToString() != "İndirme sayfasını aç") throw new Exception("Available update action missing");
        ShowUpdateResult(null);
        if (updateButton.Classes.Contains("update-available") || updateButton.Content?.ToString() != "Güncellemeleri kontrol et") throw new Exception("Stale available-update action remained");
        ShowUpdateResult(new ReleaseInfo("0.1.99"));
        Console.WriteLine("PASS Update UI distinguishes available/current results and clears stale download state");
    }
    internal void VerifyBrightnessButtons()
    {
        if (!Program.Preview) throw new InvalidOperationException("Brightness UI checks require offline preview");
        int original = runtime.Preferences.Brightness;
        for (int level = 0; level < brightnessButtons.Count; level++)
        {
            brightnessButtons[level].Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            if (runtime.Preferences.Brightness != SettingSteps.BrightnessPercent(level) || !runtime.PreviewingBrightness || runtime.Mode != OutputMode.Test)
                throw new Exception("Brightness button did not start immediate preview: " + level);
        }
        runtime.Preferences.Brightness = original;
        runtime.Save();
        Console.WriteLine("PASS All six brightness buttons start preview through their actual click handlers");
    }
    internal void PreparePreviewScroll()
    {
        Control? target = Program.Arguments.Contains("--app-preview") || Program.Arguments.Contains("--update-available-test") ? applicationCard : Program.Arguments.Contains("--color-wheel") ? ColorTestExpander : Program.Arguments.Contains("--theme-preview") ? appearanceCard : Program.Arguments.Any(a => a.StartsWith("--device-preview=")) ? DeviceCard : null;
        if (target?.TranslatePoint(new Point(0, 0), settingsScroll) is Point point) settingsScroll.Offset = new Vector(0, Math.Max(0, settingsScroll.Offset.Y + point.Y - 20));
    }
    internal void ValidateLayout() { if(noticeRow.IsVisible && noticeRow.Children.Any(c=>c.Bounds.Right>noticeRow.Bounds.Width+1))throw new Exception("Notice text obscures the dismiss action"); foreach (var panel in adaptivePanels.Where(p => p.Bounds.Width > 0)) panel.ValidateLayout(); }
    private Control RobotMark() { var canvas = new Canvas { Width = 70, Height = 86 }; void Add(Control shape, double x, double y) { Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); canvas.Children.Add(shape); } Add(new Rectangle { Width = 4, Height = 14, Fill = Brush("#708675") }, 33, 7); Add(new Ellipse { Width = 9, Height = 9, Fill = Brush("#708675") }, 30.5, 0); Add(new Rectangle { Width = 64, Height = 54, RadiusX = 17, RadiusY = 17, Fill = Brush("#D8DECF") }, 3, 18); Add(new Rectangle { Width = 48, Height = 30, RadiusX = 10, RadiusY = 10, Fill = Brush("#30473A") }, 11, 29); foreach (double x in new[] { 22.0, 41.0 }) { var eye = new Ellipse { Width = 8, Height = 10, Fill = Brush("#819588") }; eyes.Add(eye); Add(eye, x, 39); } foreach (double x in new[] { 15.0, 44.0 }) Add(new Rectangle { Width = 12, Height = 12, RadiusX = 3, RadiusY = 3, Fill = Brush("#B5C3AF") }, x, 71); return new Viewbox { Child = canvas, Width = 48, Height = 64, Margin = new Thickness(0, 0, 17, 0) }; }
    private static StackPanel Page(string title, string detail) { var panel = new StackPanel(); panel.Children.Add(Section(title, 22)); var hint = Hint(detail); hint.Margin = new Thickness(0, 6, 0, 20); panel.Children.Add(hint); return panel; }
    private static ScrollViewer Scroll(Control content)
    {
        // Avalonia's scrollbar overlays the viewport; reserve a gutter for card frames and glow.
        content.Margin = new Thickness(0, 0, 20, 0);
        return new ScrollViewer { Content = content, Margin = new Thickness(0, 0, 6, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    private static StatusCard Card(Control content) { var card = new StatusCard { Child = content, Margin = new Thickness(0, 0, 0, 14) }; card.Classes.Add("surface"); return card; }
    private static Border Divider() { var line = new Border { Height = 1, Margin = new Thickness(0, 6) }; line.Classes.Add("divider"); return line; }
    private static TextBlock Section(string text, double size = 15) { var label = Text(text, size); label.FontWeight = FontWeight.SemiBold; return label; }
    private static TextBlock Hint(string text, double size = 12) { var label = Text(text, size); label.Classes.Add("hint"); return label; }
    private static TextBlock Text(string text, double size) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static Button Action(string title, Action action) { var button = new Button { Content = title, Padding = new Thickness(13, 9), HorizontalAlignment = HorizontalAlignment.Left }; button.Click += (_, _) => action(); return button; }
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static string Label(string family) => HarnessCatalog.Label(family);
}
