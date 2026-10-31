using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using AiLight.Core;
namespace AiLight.Desktop;
public static class Program {
    public static bool Preview;
    public static string[] Arguments = [];
    public static int Run(string[] args, IDesktopPlatform platform) {
        DesktopEnvironment.Platform = platform;
        Arguments = args;
        Preview = args.Contains("--preview");
        if (args.FirstOrDefault() == "--version") {
            Console.WriteLine(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)?.InformationalVersion);
            return 0;
        }
        if (platform.HandleCommand(args) is int commandResult) return commandResult;
        if (!Preview && (platform.IsMacOS ? !OperatingSystem.IsMacOS() : !OperatingSystem.IsWindows())) {
            Console.Error.WriteLine($"Bu paket {platform.Name} içindir."); return 1;
        }
        try {
            if (!Preview && !platform.TryAcquireInstance()) return 0;
            return AppBuilder.Configure<BuddyApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        } catch (Exception error) when (Preview) {
            int index = Array.IndexOf(args, "--preview");
            if (index >= 0 && index + 1 < args.Length) File.WriteAllText(args[index + 1] + ".error.txt", error.ToString());
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown(1);
            return 1;
        } finally { platform.Dispose(); }
    }
}
public sealed class BuddyApp : Application {
    private Runtime? runtime;
    private TrayIcon? tray;
    public override void Initialize() { Name = "VibeCodingLight"; Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            Dispatcher.UIThread.UnhandledException += (_, error) => {
                if (!Program.Preview) return;
                int index = Array.IndexOf(Program.Arguments, "--preview");
                File.WriteAllText(Program.Arguments[index + 1] + ".error.txt", error.Exception.ToString());
                error.Handled = true; desktop.Shutdown(1);
            };
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
                if (Program.Arguments.Contains("--runtime-check")) SharedRuntimeChecks.Run(runtime);
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
