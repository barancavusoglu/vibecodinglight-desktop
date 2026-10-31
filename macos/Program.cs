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
        if(args.Contains("--uninstall")) {
            try{Installer.Uninstall(Storage.Load());Bridge.Send("EXIT").GetAwaiter().GetResult();return 0;}
            catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
        }
        Preview = args.Contains("--preview");
        if (!OperatingSystem.IsMacOS() && !Preview) { Console.Error.WriteLine("Bu paket macOS içindir."); return 1; }
        try {
            if (!Preview) {
                Storage.PrivateDirectory(Storage.IpcRoot);
                bool acquired=false;
                // macOS permits replacing a running bundle. Wait for its lifetime
                // lock when an updater/installer starts us during graceful exit.
                for(int attempt=0;attempt<80;attempt++) {
                    if(Bridge.Send("SHOW").GetAwaiter().GetResult())return 0;
                    try{lifetimeLock=new FileStream(Path.Combine(Storage.IpcRoot,"instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);acquired=true;break;}
                    catch(IOException){Thread.Sleep(100);}
                }
                if(!acquired)throw new IOException("Önceki uygulama henüz kapanmadı. Birkaç saniye sonra yeniden aç.");
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
    public override void Initialize() { Name = "VibeCodingLight"; Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark; }
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
                if (Program.Arguments.FirstOrDefault(a => a.StartsWith("--theme=")) is string theme) window.SetTheme(theme[8..]);
                if (Program.Arguments.FirstOrDefault(a => a.StartsWith("--preview-width=")) is string width && int.TryParse(width[16..], out int pixels)) window.Width = Math.Clamp(pixels, 420, 1800);
                if (Program.Arguments.Contains("--showcase")) {
                    runtime.Engine.Sessions.First(s => s.Harness == "codex").Title = "Build the checkout flow";
                    runtime.Engine.Apply(new("preview3", "claude", "three", "PermissionRequest", "", "tool", "", "Design system", "", false, 3, Title: "Review the landing page"));
                    runtime.Tick();
                }
                if (Program.Arguments.Contains("--empty")) { foreach (var session in runtime.Engine.Sessions.ToArray()) runtime.Engine.Remove(session.Key); runtime.Tick(); }
                if (Program.Arguments.Contains("--settings")) window.Tabs.SelectedIndex = 2;
                if (Program.Arguments.Contains("--color-wheel")) { window.Tabs.SelectedIndex = 2; window.ColorTestExpander.IsExpanded = true; }
                if (Program.Arguments.Contains("--connections")) window.Tabs.SelectedIndex = 1;
                if (Program.Arguments.FirstOrDefault(a => a.StartsWith("--device-preview=")) is string devicePreview) {
                    foreach (var state in new[] {
                        new DeviceConnectionState(DeviceTransport.Usb, "Robot USB üzerinden bağlı"),
                        new DeviceConnectionState(DeviceTransport.Bluetooth, "Robot Bluetooth üzerinden bağlı"),
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
                    runtime.PreviewDeviceState(new(transport, transport == DeviceTransport.Usb ? "Robot USB üzerinden bağlı"
                        : transport == DeviceTransport.Bluetooth ? "Robot Bluetooth üzerinden bağlı" : "USB / Bluetooth cihazı aranıyor"));
                    window.Tabs.SelectedIndex = 2;
                }
                if (Program.Arguments.Contains("--session-check")) window.VerifySessionUi();
                if (Program.Arguments.Contains("--review-ui-test")) {
                    var previous=runtime.Mode;var rgb=runtime.OutputRgb;
                    runtime.Receive(System.Text.Json.JsonSerializer.Serialize(new HookEvent("review-ui","codex","one","PermissionRequest","","review-command","Bash","Demo","",false,20)));
                    if(!runtime.PermissionReviewHeld || runtime.Mode!=previous || runtime.OutputRgb!=rgb)throw new Exception("Automatic review changed held device output");
                    runtime.ResolveAutomaticPermission("one","allow");
                    if(runtime.PermissionReviewHeld || runtime.Engine.Aggregate==Activity.NeedsInput)throw new Exception("Automatic review resolution did not release the permission");
                    Console.WriteLine("PASS Runtime review start/resolution preserves device output and releases pending permission");
                }
                if (Program.Arguments.Contains("--update-available-test")) { window.Tabs.SelectedIndex=2; window.VerifyUpdateUi(); }
                if(Program.Arguments.Contains("--notice-test")){runtime.Notice=string.Join(" ",Enumerable.Repeat("Bağlantı kaydı güncellendi. İlgili uygulamayı tamamen kapatıp yeniden aç ve yeni görev başlat.",3));runtime.Tick();}
                bool brightnessCheck = Program.Arguments.Contains("--brightness-check");
                if (brightnessCheck) window.VerifyBrightnessButtons();
                Window renderWindow=window;
                if(Program.Arguments.Contains("--diagnostics-test")){renderWindow=new DiagnosticsWindow(runtime);renderWindow.Show();}
                DispatcherTimer.RunOnce(async () => {
                    if(Program.Arguments.Contains("--verify-check"))await window.VerifyConnectionUi();
                    if (brightnessCheck) {
                        if (runtime.Testing || runtime.PreviewingBrightness || runtime.Mode != OutputMode.Off)
                            throw new Exception("Brightness preview did not restore the empty/off state after three seconds");
                        Console.WriteLine("PASS Brightness preview timer restores normal output after three seconds");
                    }
                    window.PreparePreviewScroll();
                    DispatcherTimer.RunOnce(() => {
                        if (Program.Arguments.Contains("--layout-check")) window.ValidateLayout();
                        int i = Array.IndexOf(Program.Arguments, "--preview");
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)renderWindow.Bounds.Width, (int)renderWindow.Bounds.Height));
                        bitmap.Render(renderWindow); bitmap.Save(Program.Arguments[i + 1]); desktop.Shutdown();
                    }, TimeSpan.FromMilliseconds(150));
                }, TimeSpan.FromSeconds(brightnessCheck ? 3.25 : 1));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
    private OutputMode last = OutputMode.Off;
    private static WindowIcon MakeIcon(OutputMode state) {
        var pixels=RobotIcon.Pixels(state);
        using var bitmap=new WriteableBitmap(new PixelSize(32,32),new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Unpremul);
        using(var buffer=bitmap.Lock())for(int y=0;y<32;y++)System.Runtime.InteropServices.Marshal.Copy(pixels,y*128,buffer.Address+y*buffer.RowBytes,128);
        using var stream=new MemoryStream();bitmap.Save(stream);stream.Position=0;return new WindowIcon(stream);
    }
}
