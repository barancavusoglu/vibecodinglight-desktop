namespace AiLight.App;

public static class Program {
    [STAThread]
    public static int Main(string[] args) => AiLight.Desktop.Program.Run(args, new WindowsDesktopPlatform());
}
