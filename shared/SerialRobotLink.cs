using System.IO.Ports;
using AiLight.Core;

namespace AiLight.Transport;

public sealed class SerialRobotLink : IRobotLink {
    private readonly SerialPort port;
    public string Address { get; }
    public bool IsUsb => true;
    public SerialRobotLink(string address) {
        Address = address;
        port = new SerialPort(address, 115200) {
            ReadTimeout = 700, WriteTimeout = 300, DtrEnable = true, RtsEnable = false, NewLine = "\n"
        };
        try { port.Open(); port.DiscardInBuffer(); }
        catch { port.Dispose(); throw; }
    }
    public Task<string> RequestAsync(string command, CancellationToken stop) {
        stop.ThrowIfCancellationRequested();
        port.WriteLine(command);
        return Task.FromResult(port.ReadLine().Trim());
    }
    public void Dispose() => port.Dispose();
}
