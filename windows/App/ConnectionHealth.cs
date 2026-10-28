using System.IO;
using System.Security.Cryptography;
using AiLight.Core;

namespace AiLight.App;

public record ConnectionCheck(string Family, bool Enabled, bool Healthy, string Fingerprint, string Detail);

public static class ConnectionHealth {
    public static readonly string[] Families = HarnessCatalog.Families;
    public static ConnectionCheck Check(string family, bool enabled) {
        string path = family == "opencode" ? OpenCodeIntegration.PluginPath : Integrations.ConfigPath(family);
        try {
            if (!File.Exists(path)) return new(family, enabled, false, "", enabled ? "Onarım gerekiyor · kayıt dosyası eksik" : "Etkin değil");
            if (new FileInfo(path).Length > 2_000_000) return new(family, enabled, false, "", "Kontrol edilemedi · dosya boyutu sınırı aşıldı");
            string content = File.ReadAllText(path);
            string fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
            bool healthy = family == "opencode" ? OpenCodeIntegration.Matches(content)
                : Integrations.Matches(content, family);
            string detail = healthy ? "Kayıtlar sağlıklı" : enabled ? "Onarım gerekiyor · eksik, yinelenen, kapalı veya değişmiş kayıt" : "Etkin değil";
            if (!healthy && enabled && family != "opencode" && !HarnessCatalog.IsPreview(family))
                detail = "Onarım gerekiyor · " + string.Join(", ", HookConfig.Issues(content, HookConfig.WindowsCommand(Storage.Exe, family), family));
            return new(family, enabled, healthy, fingerprint, detail);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return new(family, enabled, false, "", "Kontrol edilemedi · ayar dosyası okunamıyor");
        }
    }
}
