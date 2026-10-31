using AiLight.Core;
namespace AiLight.App;
public sealed class Device() : RobotDevice(new RobotDiscovery()) {
    public static string[] Ports() => RobotDiscovery.Ports();
}
