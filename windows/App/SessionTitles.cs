using System.IO;
using AiLight.Core;
namespace AiLight.App;
public sealed class SessionTitles {
    private IReadOnlyDictionary<string, string> titles = new Dictionary<string, string>();
    private IReadOnlySet<string>? knownIds;
    private long nextRefresh;
    private int running;
    public bool IsVisible(Session session) {
        if (session.Harness != "codex" || session.OneShot) return true;
        var known = Volatile.Read(ref knownIds);
        return known is null || known.Contains(session.Key[(session.Key.IndexOf(':') + 1)..]);
    }
    public void Initialize() => LoadIndex(new HashSet<string>());
    public string Get(Session session) {
        if (session.Harness is "opencode" or "claude" && session.Title.Length > 0) return session.Title;
        string id = session.Key[(session.Key.IndexOf(':') + 1)..];
        if (session.Harness == "codex" && Volatile.Read(ref titles).TryGetValue(id, out var title)) return title;
        return (session.Project.Length > 0 ? session.Project : "Oturum") + " · " + id[^Math.Min(8, id.Length)..];
    }
    public void Refresh(IEnumerable<Session> sessions) {
        if (Environment.TickCount64 < nextRefresh || Interlocked.Exchange(ref running, 1) != 0) return;
        nextRefresh = Environment.TickCount64 + 5000;
        var ids = sessions.Where(s => s.Harness == "codex").Select(s => s.Key[6..]).ToHashSet();
        _ = Task.Run(() => {
            try { LoadIndex(ids); }
            finally { Volatile.Write(ref running, 0); }
        });
    }
    private void LoadIndex(ISet<string> ids) {
        try {
            string path = Path.Combine(Path.GetDirectoryName(Integrations.ConfigPath("codex"))!, "session_index.jsonl");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // This optional, undocumented metadata source must never block hooks.
            if (file.Length > 16 * 1024 * 1024) return;
            using var reader = new StreamReader(file);
            IEnumerable<string> Lines() { string? line; int count = 0; while (++count <= 50000 && (line = reader.ReadLine()) != null) yield return line; }
            var catalog = TitleIndex.ReadCatalog(Lines(), ids);
            Volatile.Write(ref titles, catalog.Titles);
            Volatile.Write(ref knownIds, catalog.KnownIds.Count == 0 ? null : catalog.KnownIds);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            Volatile.Write(ref knownIds, null);
        }
    }
}
