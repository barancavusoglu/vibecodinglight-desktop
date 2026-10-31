namespace AiLight.Core;

public sealed record DevicePortChoice(string Address, string Label) {
    public override string ToString() => Label;

    public static string Canonical(string address) => address.StartsWith("/dev/tty.", StringComparison.Ordinal)
        ? "/dev/cu." + address[9..] : address;

    private static bool MacUsb(string address) => new[] { "/dev/cu.usbmodem", "/dev/cu.usbserial", "/dev/cu.wchusbserial" }
        .Any(prefix => Canonical(address).StartsWith(prefix, StringComparison.Ordinal));

    private static string Name(string address, bool macOS) {
        if (macOS && MacUsb(address)) {
            string name = Canonical(address)[8..];
            foreach (string prefix in new[] { "usbmodem", "usbserial", "wchusbserial" })
                if (name.StartsWith(prefix, StringComparison.Ordinal)) return "USB · " + name[prefix.Length..].TrimStart('-', '.');
        }
        if (address.StartsWith("BLE:", StringComparison.Ordinal)) return "Bluetooth · " + address[Math.Max(4, address.Length - 6)..];
        return macOS && address.StartsWith("/dev/", StringComparison.Ordinal) ? "Önceki bağlantı seçimi" : address;
    }

    // Selection keeps the real address; labels must never be passed to a transport.
    public static DevicePortChoice[] Create(IEnumerable<string> addresses, string selected, bool macOS, string connectedAddress = "") {
        var available = addresses.Append(connectedAddress).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.Ordinal)
            .Where(a => !macOS || MacUsb(a) || a.StartsWith("BLE:", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).GroupBy(a => macOS ? Canonical(a) : a, StringComparer.Ordinal)
            .Select(group => group.FirstOrDefault(a => a == group.Key) ?? group.First()).ToArray();
        var result = new List<DevicePortChoice> { new("", "Otomatik algıla") };
        result.AddRange(available.Select(a => new DevicePortChoice(a, Name(a, macOS)
            + (connectedAddress.Length > 0 && Matches(a, connectedAddress, macOS) ? " · bağlı" : ""))));
        if (selected.Length > 0 && !result.Any(c => Matches(c.Address, selected, macOS)))
            result.Add(new(selected, Name(selected, macOS) + " (bağlı değil)"));
        return result.ToArray();
    }

    public static bool Matches(string address, string selected, bool macOS) => macOS
        ? Canonical(address) == Canonical(selected) : address == selected;
}
