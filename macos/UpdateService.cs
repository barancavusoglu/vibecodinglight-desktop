using System.Net.Http;
using System.Text.Json;
namespace AiLight.Mac;

public sealed record ReleaseInfo(string Version);
public static class UpdateService
{
    public const string ReleasesUrl = "https://api.github.com/repos/barancavusoglu/vibecodinglight-desktop/releases?per_page=50";
    public const string DownloadPageUrl = "https://github.com/barancavusoglu/vibecodinglight-desktop/releases";
    public static async Task<ReleaseInfo?> Check()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        return await Check(client, typeof(UpdateService).Assembly.GetName().Version!);
    }
    internal static async Task<ReleaseInfo?> Check(HttpClient client, Version installed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.UserAgent.ParseAdd("VibeCodingLight-Desktop");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode(); await response.Content.LoadIntoBufferAsync(2_000_000);
        return Parse(await response.Content.ReadAsStringAsync(), installed);
    }
    internal static ReleaseInfo? Parse(string json, Version installed)
    {
        if (json.Length > 2_000_000) throw new InvalidDataException("Sürüm bilgisi fazla büyük.");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Sürüm bilgisi okunamadı.");
        Version? latest = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False || !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False) continue;
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            bool mac = assets.EnumerateArray().Any(a => a.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                && name.GetString() is { } text && text.Contains("macos-arm64", StringComparison.OrdinalIgnoreCase) && text.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (!mac) continue;
            if (!release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) continue;
            string value = tag.GetString()!;
            if (value.StartsWith("macos-v", StringComparison.Ordinal)) value = value[7..]; else if (value.StartsWith('v')) value = value[1..];
            if (value.Length > 32 || !Version.TryParse(value, out var version)) continue;
            if (latest is null || Normalize(version) > Normalize(latest)) latest = version;
        }
        if (latest is null) throw new InvalidOperationException("macOS için henüz yayımlanmış bir sürüm bulunmuyor.");
        return Normalize(latest) > Normalize(installed) ? new(latest.ToString()) : null;
    }
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
    public static void OpenDownloadPage() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DownloadPageUrl) { UseShellExecute = true });
}
