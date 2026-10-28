using AiLight.Core;

public static class FirmwareEffectRegression {
    public static async Task Run(Action<bool, string> check) {
        var effects = new FirmwareOutput();
        check(effects.Describe(OutputMode.Green, 100) == "SET_EFFECT SOLID 0 255 0", "Off-to-green stays solid on local-effect firmware");
        foreach (var previous in new[] { OutputMode.Blue, OutputMode.Yellow, OutputMode.Red }) {
            effects.Describe(previous, 100);
            check(effects.Describe(OutputMode.Green, 100) == "SET_EFFECT PULSE 0 255 0", previous + "-to-green selects one local pulse");
            check(effects.Describe(OutputMode.Green, 50) == "SET_EFFECT PULSE 0 128 0", "Brightness preserves pulse intent without host animation frames");
        }
        check(effects.Describe(OutputMode.Red, 70) == "SET_EFFECT SOLID 178 0 0", "Red interrupts local effect immediately");
        check(effects.Describe(OutputMode.Test, 70, (10, 20, 30)) == "SET_RGB 10 20 30", "Manual RGB tests keep exact selected color");
        check(effects.Describe(OutputMode.Blue, 0) == "SET_EFFECT BREATHE 0 0 0", "Zero brightness keeps a stable local effect command");
        check(effects.Describe(OutputMode.Off, 100) == "OFF", "Idle timeout can stop local animation with OFF");
        check(!DeviceProtocol.HasCapability("CAPS=RGB8,WATCHDOG,FX10", "FX1"), "Capability negotiation requires exact FX1 token");

        using var stop = new CancellationTokenSource(3000);
        var native = new Discovery(true);
        using var connection = new RobotConnection(native);
        var start = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        const string breath = "SET_EFFECT BREATHE 0 0 178";
        for (int i = 0; i <= 120; i++) {
            await connection.TickAsync("", "", breath, start.AddMilliseconds(i * 50), stop.Token);
        }
        check(native.Commands.SequenceEqual(new[] { "INFO", breath, "PING", "PING", "PING" }),
            "120 animation ticks send one effect plus three heartbeats, never individual frames");
        await connection.TickAsync("", "", "SET_EFFECT BREATHE 0 0 100", start.AddSeconds(7), stop.Token);
        check(native.Commands.Last() == "SET_EFFECT BREATHE 0 0 100", "Brightness update sends fresh intent immediately");
        await connection.TickAsync("", "", "SET_EFFECT BREATHE 0 0 100", start.AddSeconds(14), stop.Token);
        check(native.Commands.Last() == "SET_EFFECT BREATHE 0 0 100", "After sleep/watchdog gap resend state instead of a powerless PING");
        await connection.TickAsync("", "", "SET_EFFECT SOLID 178 0 0", start.AddSeconds(14.05), stop.Token);
        check(native.Commands.Last() == "SET_EFFECT SOLID 178 0 0", "Permission red preempts breath without waiting for heartbeat");
        await connection.TickAsync("", "", "SET_EFFECT PULSE 0 178 0", start.AddSeconds(15), stop.Token);
        await connection.TickAsync("", "", "SET_EFFECT PULSE 0 178 0", start.AddSeconds(17), stop.Token);
        check(native.Commands.TakeLast(2).SequenceEqual(new[] { "SET_EFFECT PULSE 0 178 0", "PING" }), "Completed local green pulse is not restarted by heartbeat");
        native.BadPong = true;
        await connection.TickAsync("", "", "SET_EFFECT PULSE 0 178 0", start.AddSeconds(19), stop.Token);
        check(!connection.Connected, "Missing heartbeat acknowledgement still drops unhealthy link");
        native.BadPong = false;
        await connection.TickAsync("", "", breath, start.AddSeconds(20), stop.Token);
        check(connection.Connected && native.Commands.TakeLast(2).SequenceEqual(new[] { "INFO", breath }), "Reconnect negotiates capabilities and reapplies complete effect");

        var legacy = new Discovery(false);
        using var oldConnection = new RobotConnection(legacy);
        await oldConnection.TickAsync("", "", breath, start, stop.Token);
        check(!oldConnection.Connected && legacy.Commands.SequenceEqual(new[] { "INFO" })
            && oldConnection.Status.Contains("Firmware güncellemesi gerekiyor"),
            "Outdated firmware receives no control commands and shows an update message");
        await oldConnection.TickAsync("", "", breath, start.AddSeconds(1), stop.Token);
        check(oldConnection.Status.Contains("Firmware güncellemesi gerekiyor") && legacy.Commands.Count == 1,
            "Firmware mismatch message survives between scans without RGB fallback");

    }
    sealed class Discovery(bool localEffects) : IRobotDiscovery {
        public List<string> Commands = [];
        public bool BadPong;
        public IReadOnlyList<string> UsbCandidates() => ["COM_TEST"];
        public IReadOnlyList<string> BleCandidates() => [];
        public string BluetoothStatus => "test";
        public Task<IRobotLink> OpenAsync(string address, CancellationToken stop) => Task.FromResult<IRobotLink>(new Link(this));
        public void Dispose() { }
        sealed class Link(Discovery source) : IRobotLink {
            public string Address => "COM_TEST";
            public bool IsUsb => true;
            public Task<string> RequestAsync(string command, CancellationToken stop) {
                source.Commands.Add(command);
                string info = "AI_LIGHT PROTOCOL=1 FW=0.3.0 ID=001122AABBCC CAPS=RGB8,WATCHDOG" + (source.Effects ? ",FX1" : "") + " WATCHDOG_MS=6000 READY=1";
                return Task.FromResult(command == "INFO" ? info : command == "PING" && !source.BadPong ? "PONG" : "OK");
            }
            public void Dispose() { }
        }
        private bool Effects => localEffects;
    }
}
