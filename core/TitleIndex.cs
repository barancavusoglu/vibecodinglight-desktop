using System.Text.Json;
namespace AiLight.Core;
public static class TitleIndex {
    public static Dictionary<string, string> Read(IEnumerable<string> lines, ISet<string> ids) => ReadCatalog(lines, ids).Titles;

    public static (HashSet<string> KnownIds, Dictionary<string, string> Titles) ReadCatalog(IEnumerable<string> lines, ISet<string> ids) {
        var knownIds = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<string, string>();
        var dates = new Dictionary<string, DateTimeOffset>();
        foreach (string line in lines) {
            if (line.Length > 65536) continue;
            try {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String) continue;
                string id = idValue.GetString()!;
                if (id.Length == 0) continue;
                knownIds.Add(id);
                if (!ids.Contains(id) || !root.TryGetProperty("thread_name", out var name) || name.ValueKind != JsonValueKind.String) continue;
                string title = string.Concat((name.GetString() ?? "").Where(c => !char.IsControl(c))).Trim();
                if (title.Length == 0) continue;
                DateTimeOffset date = default;
                if (root.TryGetProperty("updated_at", out var updated) && updated.ValueKind == JsonValueKind.String) DateTimeOffset.TryParse(updated.GetString(), out date);
                if (dates.TryGetValue(id, out var prior) && date < prior) continue;
                dates[id] = date; result[id] = title[..Math.Min(240, title.Length)];
            } catch (JsonException) { }
        }
        return (knownIds, result);
    }
}
