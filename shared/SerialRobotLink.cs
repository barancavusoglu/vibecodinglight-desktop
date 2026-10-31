using System.IO;
using System.IO.Ports;
using AiLight.Core;

namespace AiLight.Transport;

public sealed class SerialRobotLink : IRobotLink {
    private readonly SerialPort port;
    private readonly Stream? stream;
    public string Address { get; }
    public bool IsUsb => true;
    public SerialRobotLink(string address) : this(new SerialPort(address, 115200) {
            ReadTimeout = 700, WriteTimeout = 300, DtrEnable = true, RtsEnable = false, NewLine = "\n"
        }) { }
    internal SerialRobotLink(SerialPort configuredPort) {
        port = configuredPort; Address = port.PortName;
        try { port.Open(); stream = port.BaseStream; port.DiscardInBuffer(); }
        catch { Dispose(); throw; }
    }
    public Task<string> RequestAsync(string command, CancellationToken stop) {
        stop.ThrowIfCancellationRequested();
        port.WriteLine(command);
        return Task.FromResult(port.ReadLine().Trim());
    }
    public void Dispose() {
        // SerialPort.Dispose flushes before closing. On macOS a removed device
        // can leave that flush waiting forever on an abandoned write queue.
        // Closing the native stream first cancels pending I/O without draining it.
        try { if (OperatingSystem.IsMacOS()) stream?.Dispose(); }
        finally { port.Dispose(); }
    }
}
