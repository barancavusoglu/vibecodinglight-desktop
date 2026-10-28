using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;

namespace AiLight.App;
public record ReleaseInfo(string Version);

// Metadata can only announce a version. It cannot supply a command or URL.
public static class UpdateService {
    public const string ManifestUrl = "https://www.vibecodinglight.com/releases/windows/stable.json";
    public const string DownloadPageUrl = "https://www.vibecodinglight.com/#setup";
    private const int MaxManifestBytes = 32768;
    public static async Task<ReleaseInfo?> Check() {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        return await Check(client, typeof(UpdateService).Assembly.GetName().Version!);
    }
    internal static async Task<ReleaseInfo?> Check(HttpClient client, Version installed) {
        using var response = await client.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Windows için henüz yayımlanmış sürüm bilgisi bulunmuyor.");
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(MaxManifestBytes);
        return Parse(await response.Content.ReadAsStringAsync(), installed);
    }
    internal static ReleaseInfo? Parse(string text, Version installed) {
        if (text.Length > MaxManifestBytes) throw new InvalidDataException("Sürüm bilgisi fazla büyük.");
        ReleaseInfo? release;
        try {
            release = JsonSerializer.Deserialize<ReleaseInfo>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        } catch (JsonException ex) {
            throw new InvalidDataException("Sürüm bilgisi okunamadı.", ex);
        }
        if (release is null || string.IsNullOrWhiteSpace(release.Version)
            || release.Version.Length > 32 || !Version.TryParse(release.Version, out var version))
            throw new InvalidDataException("Geçerli bir sürüm bilgisi bulunamadı.");
        static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        return Normalize(version) > Normalize(installed) ? new ReleaseInfo(version.ToString()) : null;
    }
    public static void OpenDownloadPage() => Process.Start(new ProcessStartInfo(DownloadPageUrl) { UseShellExecute = true });
}
