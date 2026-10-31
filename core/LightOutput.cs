namespace AiLight.Core;

public enum OutputMode { Off, Blue, Yellow, Red, Green, Test }

// Codex reports both automatic review and a real approval as PermissionRequest.
// Keep the current LED mode briefly while its automatic reviewer makes a decision.
public sealed class PermissionLightGate {
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan ReviewLimit = TimeSpan.FromSeconds(12);
    private readonly Dictionary<string, DateTime> graceEndsUtc = [];
    private readonly Dictionary<string, DateTime> activeReviewsUtc = [];

    public bool Observe(HookEvent e, bool alreadyPending, StateEngine engine, DateTime utcNow) {
        if (e.Harness != "codex" || e.EventName != "PermissionRequest" || alreadyPending
            || !engine.Sessions.Any(s => s.Key == "codex:" + e.SessionId && s.Pending.Any(p => p.StartsWith("permit:", StringComparison.Ordinal)))) return false;
        graceEndsUtc[e.SessionId] = utcNow + Grace;
        return true;
    }

    public void ReviewStarted(string sessionId, DateTime utcNow) {
        if (sessionId.Length > 0) activeReviewsUtc[sessionId] = utcNow + ReviewLimit;
    }
    public void ReviewFinished(string sessionId) => activeReviewsUtc.Remove(sessionId);

    public bool Hold(StateEngine engine, OutputMode previousMode, DateTime utcNow) {
        if (previousMode is OutputMode.Red or OutputMode.Test) return false;
        var waiting = engine.Sessions.Where(s => engine.Included(s) && s.Activity == Activity.NeedsInput).ToArray();
        return waiting.Length > 0 && waiting.All(s => s.Harness == "codex" && s.Pending.Count > 0
            && s.Pending.All(token => token.StartsWith("permit:", StringComparison.Ordinal))
            && (graceEndsUtc.GetValueOrDefault(s.Key["codex:".Length..]) > utcNow
                || activeReviewsUtc.GetValueOrDefault(s.Key["codex:".Length..]) > utcNow));
    }
}

// The UI must render the same effective state that the device worker receives.
// A Codex permission under review is provisional, not yet a user-visible blocker.
public static class LightPresentation {
    public static bool CheckingPermission(Session session, bool hold) => hold
        && session.Harness == "codex" && !session.ExcludedFromLight
        && session.Activity == Activity.NeedsInput && session.Pending.Count > 0
        && session.Pending.All(token => token.StartsWith("permit:", StringComparison.Ordinal));

    public static Activity SessionActivity(Session session, bool hold, OutputMode mode) =>
        !CheckingPermission(session, hold) ? session.Activity
        : mode is OutputMode.Green or OutputMode.Yellow ? Activity.Working
        : mode == OutputMode.Blue ? Activity.Idle : Activity.Unknown;
}

// Describes intent, not individual frames. Keep PULSE selected after completion:
// repeated commands or brightness changes must not replay the one-shot effect.
public sealed class FirmwareOutput {
    private OutputMode previous = OutputMode.Off;
    private bool pulse;
    public string Describe(OutputMode mode, int brightness, (int R, int G, int B) test = default) {
        if (mode != previous) {
            pulse = mode == OutputMode.Green && previous is OutputMode.Blue or OutputMode.Yellow or OutputMode.Red;
            previous = mode;
        }
        int level = (int)Math.Round(Math.Clamp(brightness, 0, 100) * 255.0 / 100);
        return mode switch {
            OutputMode.Test => test == (0, 0, 0) ? "OFF" : $"SET_RGB {test.R} {test.G} {test.B}",
            OutputMode.Blue => $"SET_EFFECT BREATHE 0 0 {level}",
            OutputMode.Green => $"SET_EFFECT {(pulse ? "PULSE" : "SOLID")} 0 {level} 0",
            OutputMode.Red => $"SET_EFFECT SOLID {level} 0 0",
            OutputMode.Yellow => $"SET_EFFECT SOLID {level} {level} 0",
            _ => "OFF"
        };
    }
}

// The host plays this once after a verified USB handshake. The firmware only receives RGB values.
public static class ConnectionAnimation {
    public const double DurationSeconds = 2.0;

    public static (int R, int G, int B) Render((int R, int G, int B) destination,
        int brightness, double elapsed) {
        if (elapsed >= DurationSeconds) return destination;
        double seconds = Math.Max(0, elapsed);
        double hue = 285 * Math.Min(seconds / 1.7, 1);
        double sector = hue / 60;
        double secondary = 1 - Math.Abs(sector % 2 - 1);
        double level = Math.Clamp(brightness, 0, 100) * 255.0 / 100;
        var (red, green, blue) = (int)sector switch {
            0 => (level, secondary * level, 0.0),
            1 => (secondary * level, level, 0.0),
            2 => (0.0, level, secondary * level),
            3 => (0.0, secondary * level, level),
            _ => (secondary * level, 0.0, level)
        };
        double entrance = Math.Clamp(seconds / 0.18, 0, 1);
        double settle = Math.Clamp((seconds - 1.7) / 0.3, 0, 1);
        return (
            (int)Math.Round(red * entrance * (1 - settle) + destination.R * settle),
            (int)Math.Round(green * entrance * (1 - settle) + destination.G * settle),
            (int)Math.Round(blue * entrance * (1 - settle) + destination.B * settle));
    }
}
