using System.Diagnostics;
namespace AiLight.Mac;

public static class Installer
{
    public static string? Bundle => Storage.Exe.Contains(".app/Contents/MacOS/", StringComparison.Ordinal)
        ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..")) : null;
    public static string Target => Path.Combine(Storage.Home, "Applications", "VibeCodingLight.app");
    public static bool IsInstalled => Bundle is { } bundle && (string.Equals(bundle, Target, StringComparison.Ordinal) || string.Equals(bundle, "/Applications/VibeCodingLight.app", StringComparison.Ordinal));
    public static void StartInstall()
    {
        string source = Bundle ?? throw new InvalidOperationException("Kurulum için yayınlanmış .app paketini kullan.");
        if (IsInstalled) throw new InvalidOperationException("VibeCodingLight zaten bu bilgisayara kurulu.");
        string script = Path.Combine(source, "Contents", "Resources", "install.command");
        if (!File.Exists(script)) throw new IOException("Kurulum betiği eksik; tam test paketini kullan.");
        var start = new ProcessStartInfo("/bin/bash") { UseShellExecute = false }; start.ArgumentList.Add(script); start.ArgumentList.Add(source);
        Process.Start(start);
    }
    internal static void RemoveRegistrations(Preferences p) {
        foreach(string family in AiLight.Core.HarnessCatalog.Families)
            if(p.OwnedCommands.GetValueOrDefault(family)?.Count>0)Integrations.Change(p,family,false);
        if(p.StartupRegistration.Length>0)StartupService.Change(p,false);
    }
    public static void Uninstall(Preferences p) {
        if(!IsInstalled || Bundle is not {} bundle)throw new InvalidOperationException("Bu uygulama henüz kurulu değil.");
        using(var reader=System.Xml.XmlReader.Create(Path.Combine(bundle,"Contents","Info.plist"),new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Ignore,XmlResolver=null})) {
            var dict=System.Xml.Linq.XDocument.Load(reader).Root?.Element("dict");
            var key=dict?.Elements("key").FirstOrDefault(k=>k.Value=="CFBundleIdentifier");
            if((key?.NextNode as System.Xml.Linq.XElement)?.Value!="com.vibecodinglight.desktop")throw new IOException("Farklı uygulama korundu.");
        }
        RemoveRegistrations(p);
        string trash=Path.Combine(Storage.Home,".Trash");Directory.CreateDirectory(trash);
        if(new DirectoryInfo(trash).LinkTarget is not null)throw new IOException("Çöp dizini sembolik bağlantı; uygulama korundu.");
        Directory.Move(bundle,Path.Combine(trash,"VibeCodingLight-"+Guid.NewGuid().ToString("N")+".app"));
    }
}
