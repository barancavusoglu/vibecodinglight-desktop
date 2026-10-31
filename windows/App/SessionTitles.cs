using System.IO;
using AiLight.Core;
namespace AiLight.App;
public sealed class SessionTitles() : SessionCatalog(() =>
    Path.Combine(Path.GetDirectoryName(Integrations.ConfigPath("codex"))!, "session_index.jsonl"));
