namespace AiLight.Core;

// A repaired/changed registration needs a new real event, even when an older
// session still exists. Removing a session must not erase proven connectivity.
public sealed class ConnectionObservations {
    private readonly HashSet<string> observed = [];
    private readonly Dictionary<string, string> fingerprints = [];
    public bool Has(string family) => observed.Contains(family);
    public void Observe(string family) => observed.Add(family);
    public void Reset(string family) => observed.Remove(family);
    public void Checked(string family, string fingerprint) {
        if (fingerprints.TryGetValue(family, out var old) && old != fingerprint) Reset(family);
        fingerprints[family] = fingerprint;
    }
}
