using System.Text;
using AiLight.Core;

public static class TransportRegression {
    const string Info = "AI_LIGHT PROTOCOL=1 FW=0.3.0 ID=001122AABBCC CAPS=RGB8,WATCHDOG,BLE,USB_FIRST,FX1 WATCHDOG_MS=6000 READY=1";
    public static async Task Run(Action<bool, string> check) {
        check(DeviceProtocol.Identify(Info, out var id) && id == "001122AABBCC", "USB/BLE share the versioned product identity");
        check(DeviceProtocol.Identify(Info + " EXTRA=future", out _), "Future optional INFO fields are compatible");
        foreach (string bad in new[] { "ESP32 ready", Info.Replace("READY=1", "READY=0"), Info.Replace("PROTOCOL=1", "PROTOCOL=2"),
            Info.Replace("001122AABBCC", "001122AABBCZ"), Info.Replace("RGB8", "OTHER"), Info + " ID=001122AABBCC" })
            check(!DeviceProtocol.Identify(bad, out _), "Reject unverified or unhealthy product identity");

        using var timeout = new CancellationTokenSource(3000);
        var replies = new DeviceReplyBuffer();
        byte[] wire = Encoding.ASCII.GetBytes(Info + "\nOK\n");
        foreach (var chunk in wire.Chunk(7)) replies.Feed(chunk);
        check(await replies.ReadAsync(timeout.Token) == Info && await replies.ReadAsync(timeout.Token) == "OK", "MTU fragments reassemble long INFO and multiple replies");
        var invalid = new DeviceReplyBuffer(); invalid.Feed([255]);
        bool rejected = false; try { await invalid.ReadAsync(timeout.Token); } catch (IOException) { rejected = true; }
        check(rejected, "Malformed BLE data becomes a recoverable connection error");
        var disconnected = new DeviceReplyBuffer(); disconnected.Fail(new IOException("gone"));
        rejected = false; try { await disconnected.ReadAsync(timeout.Token); } catch (IOException) { rejected = true; }
        check(rejected, "BLE disconnect cannot fault the device worker with ChannelClosedException");

        var fake = new Discovery();
        using var connection = new RobotConnection(fake);
        DateTime now = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        fake.Ble = ["BLE:A"];
        await connection.TickAsync("", "", "SET_RGB 1 2 3", now, timeout.Token);
        check(connection.State.Connected && connection.State.Transport == DeviceTransport.Bluetooth
            && fake.Opened[0].Commands.SequenceEqual(new[] { "INFO", "SET_RGB 1 2 3" }), "Powerbank path: BLE handshake precedes all color output");
        check(fake.BluetoothEnabled, "BLE scanning starts only after USB discovery fails");
        fake.Usb = ["COM1"];
        await connection.TickAsync("", "", "SET_RGB 4 5 6", now.AddSeconds(3), timeout.Token);
        check(connection.State.Transport == DeviceTransport.Usb && fake.Opened[0].Disposed && fake.Opened[0].Commands.Last() == "OFF"
            && fake.Opened[1].Commands.SequenceEqual(new[] { "INFO", "SET_RGB 4 5 6" }), "USB preempts BLE and synchronizes fresh state");
        check(!fake.BluetoothEnabled, "USB ownership stops desktop BLE scanning/helper too");
        int opens = fake.Opened.Count;
        await connection.TickAsync("", "", "SET_RGB 4 5 6", now.AddSeconds(6), timeout.Token);
        check(fake.Opened.Count == opens && fake.Opened[1].Commands.Last() == "PING", "USB keeps ownership with a heartbeat");
        fake.Opened[1].Fail = true; fake.Usb = [];
        await connection.TickAsync("", "", "SET_RGB 4 5 6", now.AddSeconds(9), timeout.Token);
        check(!connection.State.Connected && connection.State.Transport == DeviceTransport.None,
            "Lost USB is closed on failed acknowledgement and clears displayed transport");
        await connection.TickAsync("", "", "SET_RGB 9 8 7", now.AddSeconds(10), timeout.Token);
        check(connection.Connected && !connection.IsUsb && fake.Opened.Last().Commands.Last() == "SET_RGB 9 8 7", "After USB loss BLE receives current state, not buffered old output");

        foreach (bool failOpen in new[] { true, false }) {
            var transition = new Discovery { Usb = ["COM1"], Ble = ["BLE:A"] };
            using var robot = new RobotConnection(transition);
            await robot.TickAsync("", "", "SET_RGB 1 2 3", now, timeout.Token);
            transition.Opened.Single().Fail = true;
            transition.Usb = [];
            await robot.TickAsync("", "", "SET_RGB 1 2 3", now.AddSeconds(2), timeout.Token);
            await robot.TickAsync("", "", "SET_RGB 1 2 3", now.AddSeconds(3), timeout.Token);
            string connectedStatus = robot.Status;
            var bleLink = transition.Opened.Last();
            long generation = robot.Generation;
            check(robot.State.Transport == DeviceTransport.Bluetooth && connectedStatus == "Robot Bluetooth üzerinden bağlı",
                "USB removal and powerbank reconnect establish a healthy BLE status");

            transition.Usb = ["COM1"];
            transition.FailUsbOpen = failOpen;
            transition.FailUsbInfo = !failOpen;
            await robot.TickAsync("", "", "SET_RGB 1 2 3", now.AddSeconds(6), timeout.Token);
            await robot.TickAsync("", "", "SET_RGB 9 8 7", now.AddSeconds(9), timeout.Token);
            check(robot.State.Transport == DeviceTransport.Bluetooth && robot.State.Detail == connectedStatus
                && robot.Generation == generation && !bleLink.Disposed
                && bleLink.Commands.TakeLast(2).SequenceEqual(new[] { "PING", "SET_RGB 9 8 7" }),
                (failOpen ? "USB open failure" : "USB INFO failure") + " preserves live BLE status, heartbeat and current color output");

            bleLink.Fail = true;
            await robot.TickAsync("", "", "SET_RGB 9 8 7", now.AddSeconds(11), timeout.Token);
            check(!robot.State.Connected && robot.Status.StartsWith("Bağlantı kesildi"),
                "A real BLE failure still clears connected state and reports disconnection after a failed USB probe");
            transition.FailUsbOpen = transition.FailUsbInfo = false;
            await robot.TickAsync("", "", "SET_RGB 9 8 7", now.AddSeconds(12), timeout.Token);
            check(robot.IsUsb && robot.Status == "Robot USB üzerinden bağlı"
                && transition.Opened.Last().Commands.SequenceEqual(new[] { "INFO", "SET_RGB 9 8 7" }),
                "Recovered USB retains priority and replaces the status with the healthy USB connection");
        }

        var stalled = new Discovery { Usb = ["COM1"], Ble = ["BLE:A"] };
        using (var recovering = new RobotConnection(stalled)) {
            await recovering.TickAsync("", "", "SET_RGB 1 2 3", now, timeout.Token);
            var lost = stalled.Opened.Single();
            lost.ReleaseDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lost.Fail = true; stalled.Usb = [];
            try {
                await recovering.TickAsync("", "", "SET_RGB 1 2 3", now.AddSeconds(3), timeout.Token).WaitAsync(TimeSpan.FromSeconds(1));
                check(!recovering.State.Connected && recovering.State.Transport == DeviceTransport.None,
                    "A stalled native USB close cannot retain a stale connected transport in either shell");
                await recovering.TickAsync("", "", "SET_RGB 9 8 7", now.AddSeconds(4), timeout.Token).WaitAsync(TimeSpan.FromSeconds(1));
                check(recovering.State.Transport == DeviceTransport.Bluetooth && stalled.BluetoothEnabled
                    && stalled.Opened.Last().Commands.SequenceEqual(new[] { "INFO", "SET_RGB 9 8 7" }),
                    "Bluetooth handshake and current output proceed even while an old USB close is stalled");
                check(!recovering.State.Detail.Contains("COM1") && !recovering.State.Detail.Contains("/dev/"),
                    "Connection presentation never exposes a serial path or hardware ID");
            } finally { lost.ReleaseDispose.TrySetResult(true); }
        }

        var settling = new Discovery { Ble = ["BLE:A"], BluetoothDiscoveryReady = false };
        using (var firstUse = new RobotConnection(settling)) {
            await firstUse.TickAsync("", "", "SET_RGB 1 0 0", now, timeout.Token);
            check(!firstUse.Connected && settling.Opened.Count == 0, "First automatic Bluetooth selection waits for the initial discovery window");
            settling.Ble = ["BLE:A", "BLE:B"]; settling.BluetoothDiscoveryReady = true;
            await firstUse.TickAsync("", "", "SET_RGB 1 0 0", now.AddSeconds(3), timeout.Token);
            check(!firstUse.Connected && settling.Opened.Count == 0 && firstUse.Status.Contains("cihaz seç"),
                "A second robot discovered during the initial window prevents arbitrary first-device selection");
        }
        var remembered = new Discovery { Ble = ["BLE:A"] };
        using (var robot = new RobotConnection(remembered)) {
            await robot.TickAsync("", "", "SET_RGB 1 0 0", now, timeout.Token);
            var verified = robot.State; long generation = robot.Generation;
            await robot.TickAsync("", verified.DeviceId, "SET_RGB 1 0 0", now.AddSeconds(1), timeout.Token);
            check(verified.Address == "BLE:A" && verified.DeviceId == "001122AABBCC" && robot.Generation == generation && !remembered.Opened[0].Disposed,
                "Verified address/identity can be remembered without interrupting an active connection");
        }
        var restored = new Discovery { Ble = ["BLE:OTHER", "BLE:OWN"] };
        restored.InfoByAddress["BLE:OTHER"] = Info.Replace("001122AABBCC", "FFEEDDCCBBAA");
        using (var robot = new RobotConnection(restored)) {
            await robot.TickAsync("", "001122AABBCC", "SET_RGB 1 0 0", now, timeout.Token);
            check(robot.Connected && robot.State.Address == "BLE:OWN" && restored.Opened[0].Commands.SequenceEqual(new[] { "INFO" })
                && restored.Opened[0].Disposed && restored.Opened[1].Commands.Last() == "SET_RGB 1 0 0",
                "Restored robot identity reconnects to its own device among two robots without controlling the other");
        }

        var multi = new Discovery { Ble = ["BLE:A", "BLE:B"] };
        using var multiple = new RobotConnection(multi);
        await multiple.TickAsync("", "", "SET_RGB 1 0 0", now, timeout.Token);
        check(!multiple.Connected && multi.Opened.Count == 0 && multiple.Status.Contains("Birden fazla"), "Multiple BLE robots require explicit selection");
        await multiple.TickAsync("BLE:B", "", "SET_RGB 0 0 1", now.AddSeconds(1), timeout.Token);
        check(multiple.Connected && multi.Opened.Single().Address == "BLE:B", "Explicit BLE selection controls only selected candidate");
        multi.Usb = ["COM1"];
        await multiple.TickAsync("BLE:B", "", "SET_RGB 0 1 0", now.AddSeconds(4), timeout.Token);
        check(multiple.IsUsb, "Even explicit BLE selection cannot override USB priority for the same robot");

        var explicitBle = new Discovery { Usb = ["COM_OTHER"], Ble = ["BLE:B"], UsbInfo = Info.Replace("001122AABBCC", "FFEEDDCCBBAA") };
        using var explicitRobot = new RobotConnection(explicitBle);
        await explicitRobot.TickAsync("BLE:B", "", "SET_RGB 0 1 0", now, timeout.Token);
        check(explicitRobot.Connected && !explicitRobot.IsUsb && explicitBle.Opened.Single().Address == "BLE:B",
            "An unverified explicit BLE choice cannot accidentally control another USB robot");
        await explicitRobot.TickAsync("BLE:B", "", "SET_RGB 0 1 0", now.AddSeconds(3), timeout.Token);
        check(!explicitRobot.IsUsb && explicitBle.Opened.Last().Commands.SequenceEqual(new[] { "INFO" }),
            "After BLE identity is established a different USB robot still receives no RGB");
        var manual = new Discovery();
        using var manualUsb = new RobotConnection(manual);
        await manualUsb.TickAsync("COM_MANUAL", "", "SET_RGB 1 0 0", now, timeout.Token);
        check(manualUsb.IsUsb && manual.Opened.Single().Address == "COM_MANUAL", "Explicit USB port works independently of automatic VID discovery");
        check(!manual.BluetoothEnabled && !manual.EverEnabled, "USB-only startup needs no Bluetooth scan or macOS Bluetooth permission");

        var mismatch = new Discovery { Usb = ["COM1"], Ble = ["BLE:A"], UsbInfo = Info.Replace("001122AABBCC", "FFEEDDCCBBAA") };
        using var pinned = new RobotConnection(mismatch);
        await pinned.TickAsync("", "001122AABBCC", "SET_RGB 10 0 0", now, timeout.Token);
        check(pinned.Connected && !pinned.IsUsb && mismatch.Opened.First().Commands.SequenceEqual(new[] { "INFO" })
            && mismatch.Opened.First().Disposed, "Different USB identity never steals a pinned robot or receives RGB");
        var broken = new Discovery { Ble = ["BLE:A"], BleInfo = "unknown" };
        using var badProduct = new RobotConnection(broken);
        await badProduct.TickAsync("", "", "SET_RGB 10 0 0", now, timeout.Token);
        check(!badProduct.Connected && broken.Opened.Single().Commands.SequenceEqual(new[] { "INFO" }), "Service UUID alone never authorizes color commands");
        int canceledOpens = broken.Opened.Count;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await badProduct.TickAsync("", "", "SET_RGB 10 0 0", now.AddSeconds(3), canceled.Token);
        check(broken.Opened.Count == canceledOpens, "Shutdown cancellation prevents new hardware opens");

        foreach (bool usb in new[] { true, false }) {
            var outdated = new Discovery { Usb = usb ? ["COM1"] : [], Ble = ["BLE:A"],
                UsbInfo = Info.Replace(",FX1", ""), BleInfo = Info.Replace(",FX1", "") };
            using var unsupported = new RobotConnection(outdated);
            await unsupported.TickAsync("", "", "SET_EFFECT BREATHE 0 0 178", now, timeout.Token);
            check(!unsupported.Connected && unsupported.Status.Contains("Firmware güncellemesi gerekiyor")
                && outdated.Opened.Count == 1 && outdated.Opened.Single().Disposed
                && outdated.Opened.Single().Commands.SequenceEqual(new[] { "INFO" }),
                (usb ? "USB" : "BLE") + " requires current firmware before any light commands");
        }
    }
    sealed class Discovery : IRobotDiscovery {
        public string[] Usb = [], Ble = [];
        public string UsbInfo = Info, BleInfo = Info;
        public List<Link> Opened = [];
        public bool BluetoothEnabled, EverEnabled;
        public bool BluetoothDiscoveryReady { get; set; } = true;
        public Dictionary<string,string> InfoByAddress = [];
        public bool FailUsbOpen, FailUsbInfo;
        public void SetBluetoothEnabled(bool enabled) { BluetoothEnabled = enabled; EverEnabled |= enabled; }
        public string BluetoothStatus => "Bluetooth robotu aranıyor";
        public IReadOnlyList<string> UsbCandidates() => Usb;
        public IReadOnlyList<string> BleCandidates() => Ble;
        public Task<IRobotLink> OpenAsync(string address, CancellationToken stop) {
            stop.ThrowIfCancellationRequested();
            if (!address.StartsWith("BLE:") && FailUsbOpen) throw new UnauthorizedAccessException("port unavailable");
            var link = new Link(address, InfoByAddress.GetValueOrDefault(address, address.StartsWith("BLE:") ? BleInfo : UsbInfo)); Opened.Add(link);
            if (link.IsUsb && FailUsbInfo) link.Fail = true;
            return Task.FromResult<IRobotLink>(link);
        }
        public void Dispose() { }
    }
    sealed class Link(string address, string info) : IRobotLink {
        public List<string> Commands = [];
        public TaskCompletionSource<bool>? ReleaseDispose;
        public bool Disposed, Fail;
        public string Address => address;
        public bool IsUsb => !address.StartsWith("BLE:");
        public Task<string> RequestAsync(string command, CancellationToken stop) {
            stop.ThrowIfCancellationRequested(); Commands.Add(command);
            if (Fail) throw new IOException("lost");
            return Task.FromResult(command == "INFO" ? info : command == "PING" ? "PONG" : "OK");
        }
        public void Dispose() { ReleaseDispose?.Task.GetAwaiter().GetResult(); Disposed = true; }
    }
}
