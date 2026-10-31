using System.Text.Json;
using AiLight.Core;

namespace AiLight.Desktop;

internal static class SharedRuntimeChecks {
    public static void Run(Runtime runtime) {
        runtime.Engine.IsVisible = _ => true;
        foreach (var session in runtime.Engine.Sessions.ToArray()) runtime.Forget(session.Key);
        long sequence = 100;
        foreach (string family in new[] { "codex", "claude", "opencode" }) {
            void Receive(string session, string name, string tool = "", string toolName = "") => runtime.Receive(JsonSerializer.Serialize(
                new HookEvent(Guid.NewGuid().ToString("N"), family, session, name, "", tool, toolName, "Preview", "", false, ++sequence)));
            void Check(OutputMode expected, string scenario) {
                if (runtime.Mode != expected) throw new Exception($"{family}: {scenario}: expected {expected}, got {runtime.Mode}");
            }
            Receive("active", "SessionStart"); Check(OutputMode.Blue, "idle session");
            Receive("active", family == "opencode" ? "OpenCodeWorking" : "UserPromptSubmit"); Check(OutputMode.Green, "working session");
            Receive("waiting", "SessionStart"); Check(OutputMode.Yellow, "working and idle");
            if (family != "opencode") Receive("waiting", "UserPromptSubmit");
            Receive("waiting", family == "opencode" ? "OpenCodeAsked" : "PreToolUse", "question:q", "AskUserQuestion");
            Check(OutputMode.Red, "real question overrides working immediately");
            runtime.ToggleLight(family + ":waiting"); Check(OutputMode.Green, "excluded question does not change the light");
            runtime.ToggleLight(family + ":waiting"); Check(OutputMode.Red, "including restores the unresolved question");
            runtime.Forget(family + ":waiting"); Check(OutputMode.Green, "removing blocker restores working");
            runtime.Forget(family + ":active"); Check(OutputMode.Off, "no remaining sessions");
            if (!runtime.HasObservedConnection(family)) throw new Exception(family + ": valid hook event was not observed");
            Console.WriteLine($"PASS Shared runtime: {family} IPC events, light priority, exclusion and connection observation");
        }
        runtime.Receive("{\"Harness\":\"unknown\"}");
        if (runtime.Mode != OutputMode.Off || runtime.Engine.Sessions.Count != 0) throw new Exception("Malformed hook wire changed the shared runtime");
    }
}
