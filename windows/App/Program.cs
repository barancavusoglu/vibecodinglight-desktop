using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AiLight.App;
public static class Program {
    [STAThread]
    public static int Main(string[] args) {
        if (args.FirstOrDefault() == "--integration-smoke" && args.Length == 2) return SmokeTest.Run(args[1]).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--hook") { if (args.Length is 2 or 3) Bridge.Hook(args[1], args.Length == 3 ? args[2] : "").GetAwaiter().GetResult(); return 0; }
        if (args.FirstOrDefault() == "--install" || Path.GetFileName(Storage.Exe).Equals("AI-Light-Setup.exe", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(Storage.Exe).Equals("VibeCodingLight-Setup.exe", StringComparison.OrdinalIgnoreCase)) {
            try { Installer.Install().GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "VibeCodingLight kurulumu"); return 1; }
        }
        if (args.FirstOrDefault() == "--uninstall") { Installer.Uninstall(); return 0; }
        if (args.FirstOrDefault() == "--remove-installed" && args.Length == 2) { Installer.RemoveInstalled(int.Parse(args[1])); return 0; }
        bool preview = args.FirstOrDefault() == "--render-preview";
        using var mutex = new Mutex(true, "Local\\" + Bridge.PipeName, out bool first);
        if (!first && !preview) { try { Bridge.Send("SHOW").GetAwaiter().GetResult(); } catch { } return 0; }
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var runtime = new Runtime(preview);
        app.Startup += (_, _) => {
            runtime.Start(args.Contains("--background"));
            if (preview) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
                if (args.Length > 2 && int.TryParse(args[2], out int tab)) runtime.Window.MainTabs.SelectedIndex = tab;
                var previewTheme = args.FirstOrDefault(a => a.StartsWith("--theme="));
                if (previewTheme is not null) {
                    string theme = Themes.Normalize(previewTheme[8..]);
                    foreach (var button in FindThemeButtons(runtime.Window))
                        if ((string?)button.Tag == theme) button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                }
                if (args.Contains("--session-test")) {
                    runtime.Engine.Restore(Storage.Read<List<AiLight.Core.Session>>("sessions.json") ?? []);
                    runtime.Titles.Refresh(runtime.Engine.Sessions);
                    await Task.Delay(500);
                    runtime.Window.Refresh();
                }
                if (args.Contains("--light-ui-test")) {
                    runtime.Engine.IsVisible = _ => true;
                    runtime.Engine.Apply(new("p1", "claude", "preview-work", "UserPromptSubmit", "", "", "", "Website", "", false, 1, Title: "Yeni ürün sayfası"));
                    runtime.Engine.Apply(new("p2", "opencode", "preview-hidden", "OpenCodeAsked", "", "question:1", "", "Robot", "", false, 2, Title: "Gövde tasarımı"));
                    runtime.Engine.ToggleLight("opencode:preview-hidden");
                    runtime.Connections["codex"] = new("codex", true, false, "test", "Onarım gerekiyor · Stop eksik/eski");
                    runtime.Connections["claude"] = new("claude", true, true, "test", "Kayıtlar sağlıklı");
                    runtime.Window.Refresh();
                    if (runtime.Engine.Aggregate != AiLight.Core.Activity.Working || runtime.Window.HealthBanner.Visibility != Visibility.Visible)
                        throw new Exception("Light inclusion/health banner UI failed");
                }
                if (args.Contains("--notice-test")) {
                    runtime.Window.Notice.Text = "Hook bağlantısı doğrulandı: oturum olayı alındı.";
                    runtime.Window.UpdateLayout();
                    if (runtime.Window.NoticePanel.Visibility != Visibility.Visible) throw new Exception("Notice did not appear");
                    runtime.Window.DismissNotice.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    runtime.Window.UpdateLayout();
                    if (runtime.Window.Notice.Text != "" || runtime.Window.NoticePanel.Visibility != Visibility.Collapsed) throw new Exception("Notice did not dismiss");
                    runtime.Window.Notice.Text = "Hook bağlantısı doğrulandı: oturum olayı alındı.";
                }
                if (args.Contains("--cards-test")) {
                    runtime.Engine.Apply(new("p3", "claude", "preview-wait", "PermissionRequest", "", "", "", "Mobil", "", false, 3, Title: "Yayın hazırlığı"));
                    runtime.Engine.Apply(new("p4", "claude", "preview-idle", "SessionStart", "", "", "", "API", "", false, 4, Title: "Testler tamamlandı"));
                    runtime.Window.Refresh();
                }
                if (args.Contains("--review-ui-test")) {
                    runtime.Engine.IsVisible = _ => true;
                    runtime.Engine.Apply(new("review-prompt", "codex", "review-ui", "UserPromptSubmit", "", "", "", "Preview", "", false, 1, Title: "İzin incelemesi"));
                    runtime.Engine.Apply(new("review-permission", "codex", "review-ui", "PermissionRequest", "", "call", "", "Preview", "", false, 2, Title: "İzin incelemesi"));
                    runtime.PreviewLightMode(AiLight.Core.OutputMode.Green, true);
                    var checking = runtime.Window.SessionsList.Items[0];
                    if (runtime.Window.Headline.Text != "İşler yolunda" || !runtime.Window.Summary.Text.Contains("İzin kontrol ediliyor")
                        || (string?)checking.GetType().GetProperty("Detail")?.GetValue(checking) != "İzin kontrol ediliyor")
                        throw new Exception("Held Codex permission displayed as a red blocker.");
                    runtime.PreviewLightMode(AiLight.Core.OutputMode.Red, false);
                    if (runtime.Window.Headline.Text != "Müdahale gerekiyor"
                        || (string?)runtime.Window.SessionsList.Items[0].GetType().GetProperty("Detail")?.GetValue(runtime.Window.SessionsList.Items[0]) != "İzin bekliyor")
                        throw new Exception("Unresolved Codex permission did not become red with the light.");
                    runtime.PreviewLightMode(AiLight.Core.OutputMode.Green, true);
                }
                if (args.FirstOrDefault(a => a.StartsWith("--device-preview=")) is string devicePreview) {
                    // Offline fixtures exercise USB/BLE/disconnect UI without opening a device.
                    foreach (var state in new[] {
                        new AiLight.Core.DeviceConnectionState(AiLight.Core.DeviceTransport.Usb, "COM5 · USB bağlı · Bluetooth kapalı"),
                        new AiLight.Core.DeviceConnectionState(AiLight.Core.DeviceTransport.Bluetooth, "Bluetooth bağlı · VibeCodingLight 001122AABBCC"),
                        new AiLight.Core.DeviceConnectionState(AiLight.Core.DeviceTransport.None, "Bağlantı kesildi · USB / Bluetooth yeniden aranıyor") }) {
                        runtime.PreviewDeviceState(state);
                        if (runtime.Window.DeviceConnectionTitle.Text != (state.Connected ? "Cihaz bağlı" : "Cihaz bağlı değil")
                            || (runtime.Window.DeviceConnectedCheck.Visibility == Visibility.Visible) != state.Connected
                            || runtime.Window.DeviceConnectionType.Text != (state.Transport == AiLight.Core.DeviceTransport.Usb ? "Bağlantı türü: USB"
                                : state.Connected ? "Bağlantı türü: Bluetooth" : "Bağlantı yok")
                            || runtime.Window.DeviceConnectionDetail.Text != state.Detail)
                            throw new Exception("Device connection card did not follow the actual connection state");
                    }
                    var transport = devicePreview[17..] switch {
                        "usb" => AiLight.Core.DeviceTransport.Usb, "bluetooth" => AiLight.Core.DeviceTransport.Bluetooth, _ => AiLight.Core.DeviceTransport.None
                    };
                    runtime.PreviewDeviceState(new(transport, transport == AiLight.Core.DeviceTransport.Usb ? "COM5 · USB bağlı · Bluetooth kapalı"
                        : transport == AiLight.Core.DeviceTransport.Bluetooth ? "Bluetooth bağlı · VibeCodingLight 001122AABBCC" : "USB / Bluetooth cihazı aranıyor"));
                    runtime.Window.MainTabs.SelectedIndex = 2;
                    runtime.Window.UpdateLayout(); runtime.Window.DeviceCard.BringIntoView();
                }
                var stableRows = runtime.Window.SessionsList.ItemsSource;
                if (args.Contains("--theme-check")) {
                    string selectedTheme = runtime.Preferences.Theme;
                    var lightMode = runtime.CurrentLightMode;
                    int brightness = runtime.Preferences.Brightness;
                    foreach (var button in FindThemeButtons(runtime.Window).ToArray()) {
                        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        if (runtime.Preferences.Theme != (string)button.Tag || runtime.Preferences.Brightness != brightness || runtime.CurrentLightMode != lightMode)
                            throw new Exception("Theme selection changed light output or did not apply");
                        foreach (var key in new[] { "Ink", "Muted" })
                            if (ThemeContrast((SolidColorBrush)runtime.Window.Resources[key], (SolidColorBrush)runtime.Window.Resources["Card"]) < 4.5)
                                throw new Exception("Theme text contrast below 4.5: " + button.Tag + " " + key);
                        foreach (bool waiting in new[] { true, false })
                            if (ThemeContrast(new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Themes.SessionColor((string)button.Tag, waiting))), (SolidColorBrush)runtime.Window.Resources["Card"]) < 4.5)
                                throw new Exception("Session status contrast below 4.5: " + button.Tag);
                    }
                    var selectedButton = FindThemeButtons(runtime.Window).Single(b => (string)b.Tag == selectedTheme);
                    selectedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if (Themes.Normalize("ocean") != "mist" || Themes.Normalize("lavender") != "mist" || runtime.Window.SettingsCards.Children.Count != 4)
                        throw new Exception("Legacy theme migration or merged application card failed");
                    stableRows = runtime.Window.SessionsList.ItemsSource;
                }
                runtime.Window.Refresh();
                if (!ReferenceEquals(stableRows, runtime.Window.SessionsList.ItemsSource)) throw new Exception("Unchanged cards were recreated");
                Window renderWindow = runtime.Window;
                if (args.FirstOrDefault(a => a.StartsWith("--preview-size=")) is string sizeArg) {
                    var dimensions = sizeArg[15..].Split('x');
                    renderWindow.Width = double.Parse(dimensions[0], System.Globalization.CultureInfo.InvariantCulture);
                    renderWindow.Height = double.Parse(dimensions[1], System.Globalization.CultureInfo.InvariantCulture);
                }
                if (args.Contains("--responsive-check")) {
                    // Offline fixtures cover every supported session family without hooks or hardware.
                    runtime.Engine.IsVisible = _ => true;
                    foreach (var family in args.Contains("--single-card-test") ? new[] { "codex" } : new[] { "codex", "claude", "opencode" })
                        for (int i = 0; i < (args.Contains("--single-card-test") ? 1 : 3); i++) runtime.Engine.Apply(new(Guid.NewGuid().ToString(), family,
                            $"layout-{family}-{i}", family == "opencode" ? "OpenCodeWorking" : "UserPromptSubmit",
                            "", "", "", "Geniş ekran düzeni", "", false, i + 20,
                            Title: $"{family} · farklı genişlikte okunabilir oturum başlığı {i + 1}"));
                    runtime.Window.Refresh();
                    renderWindow.UpdateLayout();
                    VerifyResponsivePanels(renderWindow);
                    // Check reflow in both directions without rebuilding card contents.
                    double originalWidth = renderWindow.Width;
                    renderWindow.Width = 420; renderWindow.UpdateLayout(); VerifyResponsivePanels(renderWindow);
                    renderWindow.Width = originalWidth; renderWindow.UpdateLayout(); VerifyResponsivePanels(renderWindow);
                    if (runtime.Window.MainTabs.SelectedIndex == 1) {
                        var healthSurface = (FrameworkElement)runtime.Window.ConnectionHealthCard.Template.FindName("Surface", runtime.Window.ConnectionHealthCard);
                        var integrationSurface = (FrameworkElement)runtime.Window.CodexCard.Template.FindName("Surface", runtime.Window.CodexCard);
                        if (Math.Abs(healthSurface.ActualWidth - integrationSurface.ActualWidth) > 1)
                            throw new Exception("Health and integration card surfaces have different widths");
                    }
                }
                if (args.Contains("--connection-cards-check")) {
                    runtime.Connections["codex"] = new("codex", true, false, "test", "Onarım gerekiyor · Stop eksik/eski");
                    runtime.Connections["claude"] = new("claude", true, true, "test", "Kayıtlar sağlıklı");
                    runtime.Connections["opencode"] = new("opencode", false, false, "test", "Etkin değil");
                    runtime.Window.Refresh();
                    if ((string)runtime.Window.CodexCard.Tag != "unhealthy" || (string)runtime.Window.ClaudeCard.Tag != "healthy" || (string)runtime.Window.OpenCodeCard.Tag != "disabled") throw new Exception("Connection card visual mapping failed");
                    runtime.Connections["codex"] = new("codex", false, false, "", "Etkin değil"); runtime.Window.Refresh();
                    if ((string)runtime.Window.CodexCard.Tag != "disabled") throw new Exception("Disabled connection retained error style");
                    runtime.Connections["codex"] = new("codex", true, false, "test", "Onarım gerekiyor · Stop eksik/eski"); runtime.Window.Refresh();
                }
                if (args.Contains("--compact-test")) { renderWindow.Width = 420; renderWindow.Height = 600; }
                if (args.Contains("--expand-color-test")) runtime.Window.ColorTestExpander.IsExpanded = true;
                if (args.Contains("--settings-check")) {
                    (int R, int G, int B)? previewColor = null;
                    for (int level = 0; level < AiLight.Core.SettingSteps.BrightnessLevels.Length; level++) {
                        ((System.Windows.Controls.Button)runtime.Window.BrightnessChoices.Children[level]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        if (runtime.Preferences.Brightness != AiLight.Core.SettingSteps.BrightnessPercent(level)) throw new Exception("Brightness tile selection failed");
                        if (!runtime.PreviewingBrightness) throw new Exception("Brightness selection did not start light preview");
                        if (previewColor is not null && runtime.PreviewColor != previewColor.Value) throw new Exception("Brightness comparison changed preview color");
                        previewColor = runtime.PreviewColor;
                    }
                    ((System.Windows.Controls.Button)runtime.Window.BrightnessChoices.Children[3]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (runtime.Preferences.Brightness != 70) throw new Exception("Brightness step mapping failed");
                    runtime.Window.IdleAutoOffMinutes.SelectedValue = 60;
                    if (runtime.Preferences.IdleAutoOffMinutes != 60) throw new Exception("Duration selection failed");
                    runtime.Window.IdleAutoOff.IsChecked = false;
                    runtime.Window.IdleAutoOff.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (runtime.Preferences.IdleAutoOffEnabled || runtime.Window.IdleAutoOffMinutes.IsEnabled) throw new Exception("Idle toggle failed");
                    runtime.Window.AutoUpdates.IsChecked = false;
                    runtime.Window.AutoUpdates.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (runtime.Preferences.AutoCheckUpdates) throw new Exception("Update toggle failed");
                    await Task.Delay(3200);
                    if (runtime.PreviewingBrightness || runtime.Testing) throw new Exception("Brightness preview did not end after three seconds");
                }
                if (args.Contains("--update-available-test")) {
                    runtime.Window.ShowUpdateResult(null);
                    if ((string)runtime.Window.UpdateButton.Content != "Güncellemeleri kontrol et" || runtime.Window.UpdateStatus.Text != "Güncelsin.")
                        throw new Exception("Current release UI failed");
                    runtime.Window.ShowUpdateResult(new ReleaseInfo("0.1.48"));
                    if ((string)runtime.Window.UpdateButton.Content != "İndirme sayfasını aç" || !runtime.Window.UpdateStatus.Text.Contains("Ayarların korunur"))
                        throw new Exception("Download page update UI failed");
                    runtime.Window.UpdateButton.BringIntoView();
                }
                if (args.Contains("--scroll-check")) {
                    renderWindow.UpdateLayout();
                    runtime.Window.SettingsScroll.ScrollToVerticalOffset(100); renderWindow.UpdateLayout();
                    double beforeScroll = runtime.Window.SettingsScroll.VerticalOffset;
                    runtime.Window.SettingsScroll.PageDown(); renderWindow.UpdateLayout();
                    if (runtime.Window.SettingsScroll.VerticalOffset <= beforeScroll) throw new Exception("Scrollbar paging failed");
                    runtime.Window.SettingsScroll.ScrollToHome(); renderWindow.UpdateLayout();
                }
                if (args.Contains("--verify-check")) {
                    var verify = runtime.Window.VerifyConnections();
                    if (runtime.Window.VerifyButton.IsEnabled || runtime.Window.VerifyProgress.Visibility != Visibility.Visible) throw new Exception("Verification busy feedback missing");
                    await verify;
                    if (!runtime.Window.VerifyButton.IsEnabled || runtime.Window.VerifyProgress.Visibility != Visibility.Collapsed || !runtime.Window.VerifyResult.Text.Contains("Kontrol tamamlandı")) throw new Exception("Verification result missing");
                }
                if (args.FirstOrDefault(a => a.StartsWith("--scroll=")) is string scrollArg && int.TryParse(scrollArg[9..], out int offset)) {
                    renderWindow.UpdateLayout();
                    if (runtime.Window.MainTabs.SelectedIndex == 1) runtime.Window.ConnectionsScroll.ScrollToVerticalOffset(offset);
                    else runtime.Window.SettingsScroll.ScrollToVerticalOffset(offset);
                }
                if (args.Contains("--diagnostics-test")) {
                    var working = new DiagnosticState("Yeşil", "Working", 0);
                    var waiting = new DiagnosticState("Kırmızı", "NeedsInput", 1);
                    runtime.Diagnostics.Record("PermissionRequest", "codex", "preview-session", working, waiting, 40, false, true, "", "preview-command");
                    runtime.Diagnostics.Record("PostToolUse", "codex", "preview-session", waiting, working, 40, false, true, "preview-call", "preview-command");
                    renderWindow = new DiagnosticsWindow(runtime); renderWindow.Show();
                }
                renderWindow.UpdateLayout();
                double scale = args.FirstOrDefault(a => a.StartsWith("--render-scale=")) is string scaleArg
                    ? double.Parse(scaleArg[15..], System.Globalization.CultureInfo.InvariantCulture) : 1;
                var image = new RenderTargetBitmap((int)(renderWindow.ActualWidth * scale), (int)(renderWindow.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                image.Render(renderWindow);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(args[1]); encoder.Save(output); app.Shutdown();
            }));
        };
        app.DispatcherUnhandledException += (_, e) => { System.Windows.MessageBox.Show(e.Exception.Message, "VibeCodingLight"); e.Handled = true; };
        return app.Run();
    }
    private static IEnumerable<System.Windows.Controls.Button> FindThemeButtons(DependencyObject root) {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) {
            if (child is System.Windows.Controls.Button button && button.Tag is string tag && Themes.All.Contains(tag)) yield return button;
            foreach (var nested in FindThemeButtons(child)) yield return nested;
        }
    }
    private static double ThemeContrast(SolidColorBrush foreground, SolidColorBrush background) {
        static double Luminance(System.Windows.Media.Color c) {
            static double Linear(byte value) { double v = value / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        double a = Luminance(foreground.Color), b = Luminance(background.Color);
        return (Math.Max(a,b) + 0.05) / (Math.Min(a,b) + 0.05);
    }
    private static void VerifyResponsivePanels(DependencyObject root) {
        if (root is AdaptiveCardPanel panel && panel.IsVisible) {
            var bounds = panel.Children.OfType<FrameworkElement>().Where(c => c.Visibility != Visibility.Collapsed)
                .Select(c => System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(c)).ToArray();
            if (bounds.Length > 0 && Math.Abs(bounds.Min(r => r.Left) - (panel.ActualWidth - bounds.Max(r => r.Right))) > 1)
                throw new Exception("Card group is not centered in the viewport");
            foreach (var rect in bounds) {
                if (rect.Width > 560.5 || rect.Width < 1 || rect.Left < -0.5 || rect.Right > panel.ActualWidth + 0.5)
                    throw new Exception("Responsive card exceeded its viewport or readable width");
            }
            for (int i = 0; i < bounds.Length; i++)
                for (int j = i + 1; j < bounds.Length; j++) {
                    var intersection = Rect.Intersect(bounds[i], bounds[j]);
                    if (!intersection.IsEmpty && intersection.Width > 0.5 && intersection.Height > 0.5)
                        throw new Exception("Responsive cards overlap");
                }
            if (bounds.Length > 1 && panel.ActualWidth >= 750 && Math.Abs(bounds[0].Top - bounds[1].Top) > 0.5)
                throw new Exception("Wide viewport did not produce side by side cards");
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) VerifyResponsivePanels(VisualTreeHelper.GetChild(root, i));
    }
}
