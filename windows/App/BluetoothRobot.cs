using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices.WindowsRuntime;
using AiLight.Core;
using AiLight.Transport;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace AiLight.App;

public sealed class RobotDiscovery : IRobotDiscovery {
    private readonly BluetoothLEAdvertisementWatcher watcher;
    private static readonly ConcurrentDictionary<string, DateTime> seen = new();
    private string bluetoothStatus = "Bluetooth robotu aranıyor";
    private DateTime nextRestart;
    private bool enabled;
    public string BluetoothStatus => bluetoothStatus;
    public static string[] Ports() {
        string[] usb;
        try { usb = SerialPort.GetPortNames().Order().ToArray(); } catch { usb = []; }
        return usb.Concat(CurrentBle()).ToArray();
    }
    private static string[] CurrentBle() => seen.Where(p => DateTime.UtcNow - p.Value < TimeSpan.FromSeconds(15))
        .Select(p => p.Key).Order().ToArray();
    public RobotDiscovery() {
        watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(DeviceProtocol.Service);
        watcher.Received += (_, args) => {
            // Filter on UUID, never on a friendly name alone.
            if (args.Advertisement.ServiceUuids.Contains(DeviceProtocol.Service))
                seen["BLE:" + args.BluetoothAddress.ToString("X12")] = DateTime.UtcNow;
        };
        watcher.Stopped += (_, args) => {
            if (args.Error != BluetoothError.Success) bluetoothStatus = "Bluetooth kapalı veya erişilemiyor · Windows ayarlarını kontrol et";
        };
    }
    public void SetBluetoothEnabled(bool value) {
        enabled = value;
        if (value) {
            if (watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Stopping) return;
            try { watcher.Start(); bluetoothStatus = "Bluetooth robotu aranıyor"; }
            catch { bluetoothStatus = "Bluetooth kullanılamıyor · USB ile çalışmaya devam edebilirsin"; }
        } else { try { watcher.Stop(); } catch { } }
    }
    public IReadOnlyList<string> UsbCandidates() {
        var result = new List<string>();
        try {
            string[] ports = SerialPort.GetPortNames();
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb is null) return result;
            foreach (string hardware in usb.GetSubKeyNames().Where(n => n.StartsWith("VID_303A", StringComparison.OrdinalIgnoreCase))) {
                using var devices = usb.OpenSubKey(hardware);
                if (devices is null) continue;
                foreach (string instance in devices.GetSubKeyNames()) {
                    using var parameters = devices.OpenSubKey(instance + @"\Device Parameters");
                    if (parameters?.GetValue("PortName") is string name && ports.Contains(name)) result.Add(name);
                }
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return result;
    }
    public IReadOnlyList<string> BleCandidates() {
        if (enabled && DateTime.UtcNow >= nextRestart && watcher.Status is BluetoothLEAdvertisementWatcherStatus.Aborted or BluetoothLEAdvertisementWatcherStatus.Stopped) {
            nextRestart = DateTime.UtcNow.AddSeconds(3);
            try { watcher.Start(); bluetoothStatus = "Bluetooth robotu aranıyor"; } catch { }
        }
        return CurrentBle();
    }
    public async Task<IRobotLink> OpenAsync(string address, CancellationToken stop) {
        if (!address.StartsWith("BLE:", StringComparison.Ordinal)) return new SerialRobotLink(address);
        return await BluetoothRobot.OpenAsync(address, stop);
    }
    public void Dispose() { try { watcher.Stop(); } catch { } }
}

public sealed class BluetoothRobot : IRobotLink {
    private BluetoothLEDevice? device;
    private GattDeviceService? service;
    private GattCharacteristic? rx, tx;
    private readonly DeviceReplyBuffer replies = new();
    private bool disposed;
    public string Address { get; }
    public bool IsUsb => false;
    private BluetoothRobot(string address) { Address = address; }
    public static async Task<IRobotLink> OpenAsync(string address, CancellationToken stop) {
        var link = new BluetoothRobot(address);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop); deadline.CancelAfter(3500);
        try {
            ulong bluetoothAddress = Convert.ToUInt64(address[4..], 16);
            link.device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(deadline.Token)
                ?? throw new IOException("Bluetooth cihazına erişilemiyor");
            link.device.ConnectionStatusChanged += link.ConnectionChanged;
            var services = await link.device.GetGattServicesForUuidAsync(DeviceProtocol.Service, BluetoothCacheMode.Uncached).AsTask(deadline.Token);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count != 1) throw new IOException("BLE hizmeti bulunamadı");
            link.service = services.Services[0];
            var characteristics = await link.service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(deadline.Token);
            if (characteristics.Status != GattCommunicationStatus.Success) throw new IOException("BLE özellikleri okunamadı");
            link.rx = characteristics.Characteristics.SingleOrDefault(c => c.Uuid == DeviceProtocol.Rx);
            link.tx = characteristics.Characteristics.SingleOrDefault(c => c.Uuid == DeviceProtocol.Tx);
            if (link.rx is null || link.tx is null
                || !link.rx.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                || !link.tx.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)) throw new IOException("BLE protokolü uyuşmuyor");
            link.tx.ValueChanged += link.ValueChanged;
            var subscribed = await link.tx.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(deadline.Token);
            if (subscribed != GattCommunicationStatus.Success) throw new IOException("BLE bildirimleri açılamadı");
            return link;
        } catch (Exception e) { link.Dispose(); throw new IOException("Bluetooth bağlantısı kurulamadı", e); }
    }
    private void ConnectionChanged(BluetoothLEDevice sender, object args) {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) replies.Fail(new IOException("Bluetooth bağlantısı kesildi"));
    }
    private void ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args) {
        using var reader = DataReader.FromBuffer(args.CharacteristicValue);
        byte[] data = new byte[reader.UnconsumedBufferLength]; reader.ReadBytes(data); replies.Feed(data);
    }
    public async Task<string> RequestAsync(string command, CancellationToken stop) {
        if (disposed || rx is null) throw new IOException("Bluetooth bağlantısı kapalı");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop); deadline.CancelAfter(1500);
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(command + "\n");
        try {
            for (int offset = 0; offset < bytes.Length; offset += 20) {
                using var writer = new DataWriter(); writer.WriteBytes(bytes.Skip(offset).Take(20).ToArray());
                var result = await rx.WriteValueWithResultAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse).AsTask(deadline.Token);
                if (result.Status != GattCommunicationStatus.Success) throw new IOException("BLE yazma hatası");
            }
            return await replies.ReadAsync(deadline.Token);
        } catch (System.Runtime.InteropServices.COMException e) {
            throw new IOException("Windows Bluetooth iletişim hatası", e);
        }
    }
    public void Dispose() {
        if (disposed) return; disposed = true;
        if (tx is not null) tx.ValueChanged -= ValueChanged;
        if (device is not null) device.ConnectionStatusChanged -= ConnectionChanged;
        replies.Fail(new IOException("Bluetooth bağlantısı kapatıldı"));
        service?.Dispose(); device?.Dispose();
    }
}
