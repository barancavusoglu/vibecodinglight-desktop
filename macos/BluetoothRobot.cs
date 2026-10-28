using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using System.Text.Json;
using AiLight.Core;
using AiLight.Transport;

namespace AiLight.Mac;

public sealed class RobotDiscovery : IRobotDiscovery {
    private Process? helper;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim input = new(1, 1);
    private static readonly ConcurrentDictionary<string, DateTime> seen = new();
    private TaskCompletionSource<bool>? connecting;
    private TaskCompletionSource<bool>? writing;
    private DeviceReplyBuffer? replies;
    private string bluetoothStatus = "Bluetooth robotu aranıyor";
    public string BluetoothStatus => bluetoothStatus;
    public static string[] Ports() {
        string[] usb;
        try { usb = SerialPort.GetPortNames().Order().ToArray(); } catch { usb = []; }
        return usb.Concat(CurrentBle()).ToArray();
    }
    private static string[] CurrentBle() => seen.Where(p => DateTime.UtcNow - p.Value < TimeSpan.FromSeconds(15))
        .Select(p => p.Key).Order().ToArray();
    public RobotDiscovery() { }
    public void SetBluetoothEnabled(bool enabled) {
        if (!enabled) { StopHelper(); return; }
        if (helper is { HasExited: false }) return;
        StopHelper();
        string path = Path.Combine(AppContext.BaseDirectory, "VibeCodingLight.Bluetooth");
        if (!OperatingSystem.IsMacOS()) { bluetoothStatus = "Bluetooth yalnızca macOS üzerinde çalışır"; return; }
        if (!File.Exists(path)) { bluetoothStatus = "Bluetooth yardımcısı eksik · macOS build.sh ile derle"; return; }
        try {
            helper = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            }) ?? throw new IOException("Bluetooth yardımcısı başlatılamadı");
            _ = ReadOutput(helper, lifetime.Token);
            _ = DrainErrors(helper, lifetime.Token);
        } catch { bluetoothStatus = "Bluetooth yardımcısı başlatılamadı · USB kullanılabilir"; }
    }
    private static async Task DrainErrors(Process process, CancellationToken stop) {
        try { while (await process.StandardError.ReadLineAsync(stop) is not null) { } }
        catch (Exception e) when (e is IOException or OperationCanceledException or InvalidOperationException) { }
    }
    private async Task ReadOutput(Process process, CancellationToken stop) {
        try {
            while (await process.StandardOutput.ReadLineAsync(stop) is { } line) {
                if (!ReferenceEquals(helper, process)) break;
                if (line.Length > 2048) throw new IOException("Bluetooth yardımcı yanıtı çok uzun");
                using var json = JsonDocument.Parse(line);
                string kind = json.RootElement.GetProperty("event").GetString() ?? "";
                string value = json.RootElement.GetProperty("value").GetString() ?? "";
                switch (kind) {
                    case "found" when Guid.TryParse(value, out _): seen["BLE:" + value] = DateTime.UtcNow; break;
                    case "ready": connecting?.TrySetResult(true); break;
                    case "written": writing?.TrySetResult(true); break;
                    case "data": replies?.Feed(Convert.FromBase64String(value)); break;
                    case "state": bluetoothStatus = value == "ready" ? "Bluetooth robotu aranıyor" : value; break;
                    case "error":
                    case "disconnected":
                        var error = new IOException(value.Length == 0 ? "Bluetooth bağlantısı kesildi" : value);
                        connecting?.TrySetException(error); writing?.TrySetException(error); replies?.Fail(error); break;
                }
            }
        } catch (Exception e) when (e is IOException or OperationCanceledException or InvalidOperationException
            or JsonException or FormatException or KeyNotFoundException) { }
        finally {
            if (ReferenceEquals(helper, process)) {
                bluetoothStatus = "Bluetooth yardımcısı kapandı · yeniden denenecek";
                var error = new IOException("Bluetooth yardımcısı kapandı");
                connecting?.TrySetException(error); writing?.TrySetException(error); replies?.Fail(error);
            }
        }
    }
    private async Task Send(string action, string value, CancellationToken stop) {
        await input.WaitAsync(stop);
        try {
            if (helper is null || helper.HasExited) throw new IOException(bluetoothStatus);
            await helper.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { action, value }).AsMemory(), stop);
            await helper.StandardInput.FlushAsync(stop);
        } finally { input.Release(); }
    }
    public IReadOnlyList<string> UsbCandidates() {
        try { return OperatingSystem.IsMacOS() ? Directory.GetFiles("/dev", "cu.usbmodem*").Order().ToArray() : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
    public IReadOnlyList<string> BleCandidates() => CurrentBle();
    public async Task<IRobotLink> OpenAsync(string address, CancellationToken stop) {
        if (!address.StartsWith("BLE:", StringComparison.Ordinal)) return new SerialRobotLink(address);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop); deadline.CancelAfter(3500);
        var buffer = new DeviceReplyBuffer(); replies = buffer;
        connecting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try {
            await Send("connect", address[4..], deadline.Token);
            await connecting.Task.WaitAsync(deadline.Token);
            return new BluetoothRobot(this, address, buffer);
        } catch { await CloseBle(); throw; }
        finally { connecting = null; }
    }
    internal async Task<string> Request(string command, DeviceReplyBuffer buffer, CancellationToken stop) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop); deadline.CancelAfter(1500);
        writing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try {
            await Send("write", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(command + "\n")), deadline.Token);
            await writing.Task.WaitAsync(deadline.Token);
            return await buffer.ReadAsync(deadline.Token);
        } finally { writing = null; }
    }
    internal async Task CloseBle() {
        replies?.Fail(new IOException("Bluetooth bağlantısı kapatıldı")); replies = null;
        try { using var deadline = new CancellationTokenSource(300); await Send("disconnect", "", deadline.Token); }
        catch { }
    }
    private void StopHelper() {
        var old = helper; helper = null;
        try { old?.StandardInput.Close(); if (old is { HasExited: false }) old.Kill(); } catch { }
        old?.Dispose();
    }
    public void Dispose() {
        lifetime.Cancel(); StopHelper(); lifetime.Dispose();
    }
}
public sealed class BluetoothRobot(RobotDiscovery discovery, string address, DeviceReplyBuffer replies) : IRobotLink {
    private bool disposed;
    public string Address => address;
    public bool IsUsb => false;
    public Task<string> RequestAsync(string command, CancellationToken stop) => disposed
        ? Task.FromException<string>(new IOException("Bluetooth bağlantısı kapalı")) : discovery.Request(command, replies, stop);
    public void Dispose() { if (!disposed) { disposed = true; discovery.CloseBle().GetAwaiter().GetResult(); } }
}
