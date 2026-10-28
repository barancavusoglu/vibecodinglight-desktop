using AiLight.Core;
namespace AiLight.Mac;
public sealed class Device : IDisposable {
    private readonly RobotConnection connection = new(new RobotDiscovery());
    private readonly FirmwareOutput firmwareOutput = new();
    public string Status => connection.Status;
    public string Id => connection.Id;
    public bool Connected => connection.Connected;
    public DeviceConnectionState State => connection.State;
    public static string[] Ports() => RobotDiscovery.Ports();
    public Task TickAsync(string port, string id, (int R, int G, int B) rgb, int brightness, OutputMode mode, CancellationToken stop) =>
        connection.TickAsync(port, id, firmwareOutput.Describe(mode, brightness, rgb), DateTime.UtcNow, stop);
    public void Dispose() => connection.Dispose();
}
