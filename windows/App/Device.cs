using System.Diagnostics;
using AiLight.Core;

namespace AiLight.App;
public sealed class Device : IDisposable {
    private readonly RobotConnection connection = new(new RobotDiscovery());
    private readonly FirmwareOutput firmwareOutput = new();
    private long generation, animationStarted;
    public string Status => connection.Status;
    public string Id => connection.Id;
    public bool Connected => connection.Connected;
    public DeviceConnectionState State => connection.State;
    public static string[] Ports() => RobotDiscovery.Ports();
    public async Task TickAsync(string port, string id, (int R, int G, int B) rgb,
        int brightness, OutputMode mode, CancellationToken stop) {
        string effectCommand = firmwareOutput.Describe(mode, brightness, rgb);
        if (mode is OutputMode.Red or OutputMode.Test) animationStarted = 0;
        if (animationStarted != 0) {
            double elapsed = Stopwatch.GetElapsedTime(animationStarted).TotalSeconds;
            if (elapsed < ConnectionAnimation.DurationSeconds) {
                rgb = ConnectionAnimation.Render(rgb, brightness, elapsed);
                effectCommand = rgb == (0, 0, 0) ? "OFF" : $"SET_RGB {rgb.R} {rgb.G} {rgb.B}";
            }
            else animationStarted = 0;
        }
        await connection.TickAsync(port, id, effectCommand, DateTime.UtcNow, stop);
        if (connection.Generation != generation) {
            generation = connection.Generation;
            animationStarted = mode is OutputMode.Red or OutputMode.Test ? 0 : Stopwatch.GetTimestamp();
        }
    }
    public void Dispose() => connection.Dispose();
}
