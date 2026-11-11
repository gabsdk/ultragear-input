using System.Text;

namespace UltraGearInput.Linux.Hardware;

internal sealed class DisplayInventory(SysfsScanner scanner, Func<int, byte[]?> probeEdid)
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, byte[]?> probes = [];
    private string connectorFingerprint = "";

    public IReadOnlyList<LinuxDisplay> Scan()
    {
        var connectors = scanner.Connectors();
        var buses = scanner.DisplayAdapterBuses();
        lock (gate)
        {
            var fingerprint = Fingerprint(connectors, buses);
            if (fingerprint != connectorFingerprint)
            {
                probes.Clear();
                connectorFingerprint = fingerprint;
            }
            return DisplayResolver.Resolve(connectors, buses, Probe);
        }
    }

    private byte[]? Probe(int bus)
    {
        if (!probes.TryGetValue(bus, out var edid))
        {
            edid = probeEdid(bus);
            probes[bus] = edid;
        }
        return edid;
    }

    private static string Fingerprint(IReadOnlyList<DrmConnector> connectors, IReadOnlyList<int> buses)
    {
        var builder = new StringBuilder();
        foreach (var connector in connectors)
        {
            builder.Append(connector.Name).Append(':').Append(connector.Bus).Append(':').Append(Convert.ToHexString(connector.Edid)).Append(';');
        }
        return builder.Append('|').AppendJoin(',', buses).ToString();
    }
}
