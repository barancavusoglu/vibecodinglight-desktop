using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using AiLight.Core;
namespace AiLight.Mac;
public static class Program {
    public static bool Preview;
    public static string[] Arguments = [];
    private static FileStream? lifetimeLock;
    public static System.Net.Sockets.Socket? Listener;
    [STAThread] public static int Main(string[] args) {
        Arguments = args;
        if (args.FirstOrDefault() == "--hook") { if (args.Length is 2 or 3) Bridge.Hook(args[1], args.Length == 3 ? args[2] : "").GetAwaiter().GetResult(); return 0; }
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--quit")) return Bridge.Send("EXIT").GetAwaiter().GetResult() ? 0 : 1;
        Preview = args.Contains("--preview");
        if (!OperatingSystem.IsMacOS() && !Preview) { Console.Error.WriteLine("Bu paket macOS içindir."); return 1; }
        try {
            if (!Preview) {
                if (Bridge.Send("SHOW").GetAwaiter().GetResult()) return 0;
                Storage.PrivateDirectory(Storage.IpcRoot);
                try { lifetimeLock = new FileStream(Path.Combine(Storage.IpcRoot, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { Bridge.Send("SHOW").GetAwaiter().GetResult(); return 0; }
                Listener = Bridge.Bind();
            }
            return AppBuilder.Configure<BuddyApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        } finally {
            Listener?.Dispose();
            if (lifetimeLock is not null) { try { File.Delete(Bridge.PathName); } catch { } lifetimeLock.Dispose(); }
        }
    }
}
public sealed class BuddyApp : Application {
    private Runtime? runtime;
    private TrayIcon? tray;
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            runtime = new Runtime(Program.Preview);
            var window = new MainWindow(runtime); runtime.Window = window; desktop.MainWindow = window;
            var menu = new NativeMenu();
            var show = new NativeMenuItem("VibeCodingLight’yi aç"); show.Click += (_, _) => runtime.Show(); menu.Add(show);
            var quit = new NativeMenuItem("Çıkış"); quit.Click += (_, _) => runtime.Exit(); menu.Add(quit);
            tray = new TrayIcon { ToolTipText = "VibeCodingLight", Icon = MakeIcon(OutputMode.Off), Menu = menu, IsVisible = !Program.Preview };
            tray.Clicked += (_, _) => runtime.Show();
            TrayIcon.SetIcons(this, new TrayIcons { tray });
            runtime.Changed += () => {
                tray.ToolTipText = runtime.Summary;
                if (runtime.Mode != last) { last = runtime.Mode; tray.Icon = MakeIcon(last); }
            };
            desktop.Exit += (_, _) => { runtime.Dispose(); tray.Dispose(); };
            runtime.Start(); window.Show();
            if (Program.Arguments.Contains("--background")) DispatcherTimer.RunOnce(window.Hide, TimeSpan.FromMilliseconds(100));
            if (Program.Preview) {
                if (Program.Arguments.Contains("--light")) window.SetTheme("light");
                if (Program.Arguments.Contains("--settings")) window.Tabs.SelectedIndex = 2;
                if (Program.Arguments.Contains("--connections")) window.Tabs.SelectedIndex = 1;
                if (Program.Arguments.FirstOrDefault(a => a.StartsWith("--device-preview=")) is string devicePreview) {
                    foreach (var state in new[] {
                        new DeviceConnectionState(DeviceTransport.Usb, "COM5 · USB bağlı · Bluetooth kapalı"),
                        new DeviceConnectionState(DeviceTransport.Bluetooth, "Bluetooth bağlı · VibeCodingLight 001122AABBCC"),
                        new DeviceConnectionState(DeviceTransport.None, "Bağlantı kesildi · USB / Bluetooth yeniden aranıyor") }) {
                        runtime.PreviewDeviceState(state);
                        if (window.DeviceConnectionTitle.Text != (state.Connected ? "Cihaz bağlı" : "Cihaz bağlı değil")
                            || window.DeviceConnectedCheck.IsVisible != state.Connected
                            || window.DeviceConnectionType.Text != (state.Transport == DeviceTransport.Usb ? "Bağlantı türü: USB"
                                : state.Connected ? "Bağlantı türü: Bluetooth" : "Bağlantı yok")
                            || window.DeviceConnectionDetail.Text != state.Detail)
                            throw new Exception("Device connection card did not follow the actual connection state");
                    }
                    var transport = devicePreview[17..] switch { "usb" => DeviceTransport.Usb, "bluetooth" => DeviceTransport.Bluetooth, _ => DeviceTransport.None };
                    runtime.PreviewDeviceState(new(transport, transport == DeviceTransport.Usb ? "COM5 · USB bağlı · Bluetooth kapalı"
                        : transport == DeviceTransport.Bluetooth ? "Bluetooth bağlı · VibeCodingLight 001122AABBCC" : "USB / Bluetooth cihazı aranıyor"));
                    window.Tabs.SelectedIndex = 2;
                }
                DispatcherTimer.RunOnce(() => {
                    if (Program.Arguments.Any(a => a.StartsWith("--device-preview="))) window.DeviceCard.BringIntoView();
                    int i = Array.IndexOf(Program.Arguments, "--preview");
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
                    bitmap.Render(window); bitmap.Save(Program.Arguments[i + 1]); desktop.Shutdown();
                }, TimeSpan.FromSeconds(1));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
    private OutputMode last = OutputMode.Off;
    // Small generated BMP: no external assets or network required for the menu icon.
    private static WindowIcon MakeIcon(OutputMode state) {
        const int size = 32, stride = 96; byte[] bmp = new byte[54 + stride * size];
        using var data = new MemoryStream(bmp); using var w = new BinaryWriter(data);
        w.Write((ushort)0x4D42); w.Write(bmp.Length); w.Write(0); w.Write(54); w.Write(40); w.Write(size); w.Write(size); w.Write((ushort)1); w.Write((ushort)24); w.Write(0); w.Write(stride * size); w.Write(new byte[16]);
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
            bool body = x >= 3 && x <= 28 && y >= 6 && y <= 25;
            bool eye = (x >= 9 && x <= 12 || x >= 20 && x <= 23) && y >= 13 && y <= 19;
            byte r = body ? (byte)55 : (byte)25, g = body ? (byte)65 : (byte)30, b = body ? (byte)75 : (byte)38;
            if (eye) { (r, g, b) = state switch { OutputMode.Red => ((byte)255,(byte)80,(byte)70), OutputMode.Yellow => ((byte)240,(byte)205,(byte)60), OutputMode.Green => ((byte)90,(byte)220,(byte)135), OutputMode.Blue => ((byte)80,(byte)155,(byte)240), _ => ((byte)130,(byte)140,(byte)150) }; }
            int at = 54 + y * stride + x * 3; bmp[at] = b; bmp[at + 1] = g; bmp[at + 2] = r;
        }
        return new WindowIcon(new MemoryStream(bmp));
    }
}
