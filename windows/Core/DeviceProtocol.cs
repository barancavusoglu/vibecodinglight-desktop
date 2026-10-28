using System.Text;
using System.Threading.Channels;

namespace AiLight.Core;

public static class DeviceProtocol {
    public static readonly Guid Service = new("8b7e0001-5c8a-4c8e-9f24-367b495d7a11");
    public static readonly Guid Rx = new("8b7e0002-5c8a-4c8e-9f24-367b495d7a11");
    public static readonly Guid Tx = new("8b7e0003-5c8a-4c8e-9f24-367b495d7a11");
    public static bool HasCapability(string info, string capability) => info.Split(' ')
        .Any(field => field.StartsWith("CAPS=", StringComparison.Ordinal) && field[5..].Split(',').Contains(capability));
    public static bool Identify(string info, out string id) {
        id = "";
        if (info.Length > 512 || !info.StartsWith("AI_LIGHT ", StringComparison.Ordinal)) return false;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string field in info[9..].Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
            int split = field.IndexOf('=');
            if (split <= 0 || !fields.TryAdd(field[..split], field[(split + 1)..])) return false;
        }
        if (fields.GetValueOrDefault("PROTOCOL") != "1" || fields.GetValueOrDefault("READY") != "1"
            || fields.GetValueOrDefault("WATCHDOG_MS") != "6000" || !fields.ContainsKey("FW")) return false;
        string candidate = fields.GetValueOrDefault("ID", "");
        var caps = fields.GetValueOrDefault("CAPS", "").Split(',');
        if (candidate.Length != 12 || candidate.Any(c => !char.IsAsciiHexDigit(c))
            || !caps.Contains("RGB8") || !caps.Contains("WATCHDOG")) return false;
        id = candidate.ToUpperInvariant(); return true;
    }
}

// Notifications can split a single reply even in the middle of INFO. Bounded,
// strict ASCII framing is shared by both native BLE clients, independent of MTU.
public sealed class DeviceReplyBuffer {
    private readonly StringBuilder partial = new();
    private readonly Channel<string> lines = Channel.CreateBounded<string>(new BoundedChannelOptions(8) {
        SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly object gate = new();
    public void Feed(ReadOnlySpan<byte> bytes) {
        lock (gate) foreach (byte b in bytes) {
            if (b == '\n') {
                if (partial.Length > 0 && !lines.Writer.TryWrite(partial.ToString().TrimEnd('\r')))
                    Fail(new IOException("BLE yanıt kuyruğu doldu"));
                partial.Clear();
            } else if ((b < 32 && b != '\r') || b > 126 || partial.Length >= 512) {
                partial.Clear(); Fail(new IOException("Geçersiz BLE yanıtı")); return;
            } else partial.Append((char)b);
        }
    }
    public void Fail(Exception error) => lines.Writer.TryComplete(error);
    public async Task<string> ReadAsync(CancellationToken stop) {
        try { return await lines.Reader.ReadAsync(stop); }
        catch (ChannelClosedException e) { throw new IOException("BLE yanıt bağlantısı kapandı", e); }
    }
}

public interface IRobotLink : IDisposable {
    string Address { get; }
    bool IsUsb { get; }
    Task<string> RequestAsync(string command, CancellationToken stop);
}
public enum DeviceTransport { None, Usb, Bluetooth }
public sealed record DeviceConnectionState(DeviceTransport Transport, string Detail) {
    public bool Connected => Transport != DeviceTransport.None;
}
public interface IRobotDiscovery : IDisposable {
    void SetBluetoothEnabled(bool enabled) { }
    IReadOnlyList<string> UsbCandidates();
    IReadOnlyList<string> BleCandidates();
    string BluetoothStatus { get; }
    Task<IRobotLink> OpenAsync(string address, CancellationToken stop);
}

// All harnesses produce the same desired effect. Transport selection never reads
// hook/session state. A verified USB device always preempts BLE for that robot.
public sealed class RobotConnection(IRobotDiscovery discovery) : IDisposable {
    private IRobotLink? link;
    private string selection = "", selectedId = "", pinnedId = "", lastCommand = "";
    private DateTime nextScan, lastContact;
    private bool disposed;
    public string Status { get; private set; } = "USB / Bluetooth cihazı aranıyor";
    public string Id { get; private set; } = "";
    public bool Connected => link is not null;
    public bool IsUsb => link?.IsUsb == true;
    public DeviceConnectionState State => new(link is null ? DeviceTransport.None
        : link.IsUsb ? DeviceTransport.Usb : DeviceTransport.Bluetooth, Status);
    private bool firmwareUpdateRequired;
    public long Generation { get; private set; }
    private sealed record VerifiedLink(IRobotLink Link, string Id);
    private async Task<VerifiedLink?> Verify(string address, CancellationToken stop) {
        IRobotLink? candidate = null;
        try {
            candidate = await discovery.OpenAsync(address, stop);
            string info = await candidate.RequestAsync("INFO", stop);
            if (!DeviceProtocol.Identify(info, out string id)
                || pinnedId.Length > 0 && !string.Equals(id, pinnedId, StringComparison.OrdinalIgnoreCase)) return null;
            if (!DeviceProtocol.HasCapability(info, "FX1")) {
                firmwareUpdateRequired = true;
                Status = "Firmware güncellemesi gerekiyor · 0.3.0 veya sonrası (FX1)";
                return null;
            }
            var verified = new VerifiedLink(candidate, id);
            candidate = null; return verified;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException
            or InvalidOperationException or OperationCanceledException or ArgumentException) {
            Status = address.StartsWith("BLE:", StringComparison.Ordinal) ? "Bluetooth bağlantısı kurulamadı" : "USB portu açılamadı · Seri Monitör'ü kapat";
            return null;
        } finally { candidate?.Dispose(); }
    }
    public async Task TickAsync(string chosen, string chosenId, string command,
        DateTime now, CancellationToken stop) {
        if (disposed || stop.IsCancellationRequested) return;
        if (selection != chosen || selectedId != chosenId) {
            await DisconnectAsync(); selection = chosen; selectedId = chosenId;
            pinnedId = chosenId; nextScan = DateTime.MinValue;
        }
        try {
            if (now >= nextScan && link?.IsUsb != true) {
                nextScan = now.AddSeconds(3);
                firmwareUpdateRequired = false;
                string[] usb = discovery.UsbCandidates().Distinct().ToArray();
                if (selection.Length > 0 && !selection.StartsWith("BLE:", StringComparison.Ordinal))
                    usb = [selection]; // Preserve explicit non-Espressif/manual USB port support.
                if (selection.StartsWith("BLE:", StringComparison.Ordinal) && pinnedId.Length == 0)
                    usb = []; // First establish which physical robot the explicit BLE address identifies.
                if (usb.Length > 1 && pinnedId.Length == 0) {
                    Status = "Birden fazla USB cihazı var · cihaz seç"; return;
                }
                foreach (string address in usb) {
                    // Try USB before dropping BLE. Successful verification pins identity.
                    var candidate = await Verify(address, stop);
                    if (candidate is null) continue;
                    await DisconnectAsync(); Adopt(candidate); break;
                }
                if (firmwareUpdateRequired) { await DisconnectAsync(); return; }
                if (link is null && !stop.IsCancellationRequested) {
                    discovery.SetBluetoothEnabled(true);
                    var ble = discovery.BleCandidates().Distinct().ToArray();
                    if (selection.StartsWith("BLE:", StringComparison.Ordinal)) ble = ble.Where(p => p == selection).ToArray();
                    if (selection.Length > 0 && !selection.StartsWith("BLE:", StringComparison.Ordinal) && pinnedId.Length == 0)
                        ble = []; // An unverified manual USB choice must not control a different robot.
                    if (ble.Length > 1 && pinnedId.Length == 0) {
                        Status = "Birden fazla Bluetooth robotu var · cihaz seç"; return;
                    }
                    foreach (string address in ble) {
                        var candidate = await Verify(address, stop);
                        if (candidate is null) continue;
                        Adopt(candidate); break;
                    }
                }
                if (link is null && !firmwareUpdateRequired) Status = "USB cihazı yok · " + discovery.BluetoothStatus;
            }
            if (link is null) return;
            if (command != lastCommand || (now - lastContact).TotalSeconds >= 2) {
                // A long scheduling/sleep gap can outlive the firmware watchdog:
                // resend full intent then, because PING must never revive expired state.
                bool ping = command == lastCommand
                    && now - lastContact < TimeSpan.FromSeconds(4);
                if (await link.RequestAsync(ping ? "PING" : command, stop) != (ping ? "PONG" : "OK"))
                    throw new IOException("Komut onaylanmadı");
                lastCommand = command; lastContact = now;
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException
            or InvalidOperationException or OperationCanceledException or ArgumentException) {
            await DisconnectAsync(); Status = "Bağlantı kesildi · USB / Bluetooth yeniden aranıyor";
            nextScan = DateTime.MinValue;
        }
    }
    private void Adopt(VerifiedLink candidate) {
        firmwareUpdateRequired = false;
        link = candidate.Link; Id = pinnedId = candidate.Id;
        lastCommand = ""; Generation++;
        discovery.SetBluetoothEnabled(!link.IsUsb);
        Status = link.IsUsb ? link.Address + " · USB bağlı · Bluetooth kapalı"
            : "Bluetooth bağlı · VibeCodingLight " + Id;
    }
    private async Task DisconnectAsync() {
        var old = link; link = null; lastCommand = "";
        if (old is null) return;
        try { using var stop = new CancellationTokenSource(350); await old.RequestAsync("OFF", stop.Token); }
        catch { /* Firmware watchdog also bounds a lost connection. */ }
        finally { old.Dispose(); }
    }
    public void Dispose() {
        if (disposed) return; disposed = true;
        DisconnectAsync().GetAwaiter().GetResult(); discovery.Dispose();
    }
}
