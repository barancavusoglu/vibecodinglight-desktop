using AiLight.Core;
namespace AiLight.Mac;
public sealed class Device() : RobotDevice(new RobotDiscovery()) {
    public static string[] Ports() => RobotDiscovery.Ports();
}
