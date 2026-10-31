namespace AiLight.Core;

public static class HarnessCatalog {
    public static readonly string[] Families = ["codex", "claude", "opencode", "copilot", "antigravity"];
    public static bool IsPreview(string family) => family is "copilot" or "antigravity";
    public static string Label(string family) => family switch {
        "codex" => "Codex", "claude" => "Claude Code", "opencode" => "OpenCode",
        "copilot" => "GitHub Copilot", "antigravity" => "Antigravity", _ => family
    };
    public const string PreviewNote = "Beta · çalışma/boşta takibi. İzin ve soru bekleme takibi henüz desteklenmiyor.";
}
