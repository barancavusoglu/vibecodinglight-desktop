using System.Security.Cryptography;
using System.Text;
using AiLight.Core;
namespace AiLight.Mac;
public sealed record ConnectionCheck(string Family, bool Enabled, bool Healthy, string Fingerprint, string Detail);
public static class ConnectionHealth {
    public static ConnectionCheck Check(string family, bool enabled) {
        try {
            string path = Integrations.ConfigPath(family);
            if (!File.Exists(path)) return new(family,enabled,false,"",enabled?"Onarım gerekiyor · kayıt dosyası eksik":"Etkin değil");
            if (new FileInfo(path).Length > 2_000_000) return new(family,enabled,false,"","Kontrol edilemedi · dosya boyutu sınırı aşıldı");
            string content=File.ReadAllText(path);
            string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            bool healthy=Integrations.Matches(content,family);
            string detail=healthy?"Kayıtlar sağlıklı":enabled?"Onarım gerekiyor · eksik, yinelenen, kapalı veya değişmiş kayıt":"Etkin değil";
            if(!healthy && enabled && family is "codex" or "claude")
                detail="Onarım gerekiyor · "+string.Join(", ",HookConfig.Issues(content,Integrations.Command(Storage.Exe,family),family));
            return new(family,enabled,healthy,fingerprint,detail);
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException) {
            return new(family,enabled,false,"","Kontrol edilemedi · ayar dosyası okunamıyor");
        }
    }
}
