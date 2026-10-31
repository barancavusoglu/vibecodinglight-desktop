using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AiLight.Core;
namespace AiLight.Mac;

public sealed class DiagnosticsWindow : Window
{
    private readonly DiagnosticJournal diagnostics;
    private readonly TextBox output;
    private readonly TextBlock status;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int revision = -1;
    public DiagnosticsWindow(Runtime runtime)
    {
        diagnostics = runtime.Diagnostics; Title = "VibeCodingLight · Tanı günlüğü"; Width = 760; Height = 600; MinWidth = 520; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var palette = MacThemes.Palette(runtime.Preferences.Theme); Background = Brush(palette[0]); Foreground = Brush(palette[1]);
        var root = new DockPanel { Margin = new Thickness(20) }; var header = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(Text("Işık neden renk değiştirdi?", 20)); header.Children.Add(Text("Son 500 olay · yalnızca bu bilgisayarda. Mesaj, komut, cevap, başlık ve klasör içerikleri kaydedilmez. Kimlikler kısa özetlerle gösterilir."));
        header.Children.Add(Text("PermissionRequest bir izin değerlendirmesidir; otomatik onay mı, gerçek kullanıcı onayı mı bu olaydan kesin bilinmez. Renkler hedef durumdur; bağlantı ve test bilgisi ayrıca yazılır.")); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) }; status = Text(""); footer.Children.Add(status); var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var copy = new Button { Content = "Görünen kayıtları kopyala", Padding = new Thickness(12, 7), CornerRadius = new CornerRadius(9) };
        var clear = new Button { Content = "Günlüğü temizle", Padding = new Thickness(12, 7), CornerRadius = new CornerRadius(9) }; buttons.Children.Add(copy); buttons.Children.Add(clear); footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        output = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Menlo"), FontSize = 12, Background = Brush(palette[5]), Foreground = Brush(palette[1]), Padding = new Thickness(12) }; ScrollViewer.SetVerticalScrollBarVisibility(output, ScrollBarVisibility.Auto); root.Children.Add(output); Content = root;
        copy.Click += async (_, _) => { try { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(output.Text ?? ""); status.Text = "Panoya kopyalandı."; } catch { status.Text = "Panoya kopyalanamadı."; } };
        clear.Click += async (_, _) =>
        {
            var confirm = new Window { Title = "Tanı günlüğü", Width = 400, Height = 180, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var content = new StackPanel { Margin = new Thickness(20), Spacing = 16 }; content.Children.Add(Text("Yerel tanı günlüğü silinsin mi? Oturumlar ve ayarlar korunur. Bu işlem geri alınamaz.")); var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; var cancel = new Button { Content = "Vazgeç" }; var yes = new Button { Content = "Temizle" }; cancel.Click += (_, _) => confirm.Close(false); yes.Click += (_, _) => confirm.Close(true); choices.Children.Add(cancel); choices.Children.Add(yes); content.Children.Add(choices); confirm.Content = content;
            if (await confirm.ShowDialog<bool>(this)) { diagnostics.Clear(); RefreshLog(); }
        };
        timer.Tick += (_, _) => RefreshLog(); Closed += (_, _) => timer.Stop(); timer.Start(); RefreshLog();
    }
    private void RefreshLog() { if (revision != diagnostics.Revision) { revision = diagnostics.Revision; var entries = diagnostics.Snapshot(); output.Text = entries.Length == 0 ? "Henüz olay yok. Yeni olaylar burada görünecek." : string.Join("\n\n", entries.Reverse().Select(e => $"{e.Time.ToLocalTime():dd.MM HH:mm:ss.fff}  {e.Harness}  {e.Event}\n  Işık: {e.Before.Light} → {e.After.Light} | Oturum: {e.Before.Session} → {e.After.Session} | Bekleyen: {e.Before.Pending} → {e.After.Pending}\n  Oturum #{e.SessionTag}  İstek #{e.RequestTag}  Komut izi #{e.CommandTag}\n  Parlaklık %{e.Brightness} · Cihaz {(e.Connected ? "bağlı" : "bağlı değil")} · Renk testi {(e.Testing ? "açık" : "kapalı")}")); status.Text = $"{entries.Length}/{DiagnosticJournal.Capacity} kayıt · en yeni üstte · diagnostics.json"; } if (diagnostics.Error.Length > 0) status.Text = diagnostics.Error; }
    private static TextBlock Text(string text, double size = 12) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
