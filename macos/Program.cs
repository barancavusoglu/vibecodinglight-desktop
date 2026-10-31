namespace AiLight.Mac;

public static class Program {
    [STAThread]
    public static int Main(string[] args) => AiLight.Desktop.Program.Run(args, new MacDesktopPlatform());
}
