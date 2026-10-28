using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AiLight.Core;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
namespace AiLight.App;
public sealed class DiagnosticsWindow : Window {
    private readonly Diagnostics diagnostics;
    private readonly TextBox output;
    private readonly TextBlock status;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int revision = -1;
    public DiagnosticsWindow(Runtime runtime) {
        diagnostics = runtime.Diagnostics;
        Title = "VibeCodingLight · Tanı günlüğü"; Width = 760; Height = 600; MinWidth = 520; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Themes.Apply(this, runtime.Preferences.Theme);
        Background = (Brush)Resources["Page"]; Foreground = (Brush)Resources["Ink"];
        SourceInitialized += (_, _) => Themes.Apply(this, runtime.Preferences.Theme);
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Margin = new Thickness(0,0,0,12) };
        header.Children.Add(new TextBlock { Text = "Işık neden renk değiştirdi?", FontSize = 20, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Son 500 olay · yalnızca bu bilgisayarda. Mesaj, komut, cevap, başlık ve klasör içerikleri kaydedilmez. Kimlikler kısa özetlerle gösterilir.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) });
        header.Children.Add(new TextBlock { Text = "PermissionRequest bir izin değerlendirmesidir; otomatik onay mı, gerçek kullanıcı onayı mı bu olaydan kesin bilinmez. Renkler hedef durumdur; bağlantı ve test bilgisi ayrıca yazılır.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) });
        DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(0,12,0,0) };
        status = new TextBlock { TextWrapping = TextWrapping.Wrap }; footer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,8,0,0) };
        var copy = new Button { Content = "Görünen kayıtları kopyala", Padding = new Thickness(12,7,12,7), Margin = new Thickness(0,0,8,0) };
        var clear = new Button { Content = "Günlüğü temizle", Padding = new Thickness(12,7,12,7) };
        foreach (var button in new[] { copy, clear }) { button.Background = (Brush)Resources["Control"]; button.Foreground = (Brush)Resources["Ink"]; buttons.Children.Add(button); }
        footer.Children.Add(buttons); DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
        output = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"), FontSize = 12, Background = (Brush)Resources["Card"], Foreground = (Brush)Resources["Ink"], BorderThickness = new Thickness(0), Padding = new Thickness(12) };
        root.Children.Add(output); Content = root;
        copy.Click += (_, _) => { try { System.Windows.Clipboard.SetText(output.Text); status.Text = "Panoya kopyalandı."; } catch { status.Text = "Panoya kopyalanamadı."; } };
        clear.Click += (_, _) => {
            if (MessageBox.Show(this, "Yerel tanı günlüğü silinsin mi? Oturumlar ve ayarlar korunur. Bu işlem geri alınamaz.", "Tanı günlüğü", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            diagnostics.Clear(); RefreshLog();
        };
        timer.Tick += (_, _) => RefreshLog(); timer.Start(); Closed += (_, _) => timer.Stop(); RefreshLog();
    }
    public static string Format(DiagnosticEntry e) =>
        $"{e.Time.ToLocalTime():dd.MM HH:mm:ss.fff}  {e.Harness}  {e.Event}\n" +
        $"  Işık: {e.Before.Light} → {e.After.Light} | Oturum: {e.Before.Session} → {e.After.Session} | Bekleyen: {e.Before.Pending} → {e.After.Pending}\n" +
        $"  Oturum #{e.SessionTag}  İstek #{e.RequestTag}  Komut izi #{e.CommandTag}\n" +
        $"  Parlaklık %{e.Brightness} · Cihaz {(e.Connected ? "bağlı" : "bağlı değil")} · Renk testi {(e.Testing ? "açık" : "kapalı")}";
    private void RefreshLog() {
        if (revision != diagnostics.Revision) {
            revision = diagnostics.Revision;
            var entries = diagnostics.Snapshot();
            output.Text = entries.Length == 0 ? "Henüz olay yok. Yeni olaylar burada görünecek." : string.Join("\n\n", entries.Reverse().Select(Format));
            status.Text = $"{entries.Length}/{Diagnostics.Capacity} kayıt · en yeni üstte · diagnostics.json";
        }
        if (diagnostics.Error.Length > 0) status.Text = diagnostics.Error;
    }
}
