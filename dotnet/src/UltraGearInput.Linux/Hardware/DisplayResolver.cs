using System.Globalization;
using UltraGearInput.Core;

namespace UltraGearInput.Linux.Hardware;

internal sealed record LinuxDisplay(DisplayDescriptor Descriptor, string Connector, int? Bus);

internal static class DisplayResolver
{
    public static IReadOnlyList<LinuxDisplay> Resolve(
        IReadOnlyList<DrmConnector> connectors,
        IReadOnlyList<int> adapterBuses,
        Func<int, byte[]?> probeEdid)
    {
        var withEdid = connectors.Where(connector => connector.IsConnected && Edid.IsValid(connector.Edid)).ToList();
        var found = withEdid
            .Where(connector => connector.Bus is not null)
            .Select(connector => (connector.Name, connector.Edid, connector.Bus))
            .ToList();

        var unlinked = withEdid.Where(connector => connector.Bus is null).ToList();
        var probeAll = connectors.Count == 0;
        if (unlinked.Count > 0 || probeAll)
        {
            var used = found.Select(display => display.Bus).ToHashSet();
            foreach (var bus in adapterBuses.Where(bus => !used.Contains(bus)))
            {
                if (probeEdid(bus) is not { } edid || !Edid.IsValid(edid))
                {
                    continue;
                }
                var match = unlinked.FirstOrDefault(connector => Edid.SameBaseBlock(connector.Edid, edid));
                if (match is not null)
                {
                    unlinked.Remove(match);
                    found.Add((match.Name, match.Edid, bus));
                }
                else if (probeAll)
                {
                    found.Add(($"i2c-{bus.ToString(CultureInfo.InvariantCulture)}", edid, bus));
                }
            }
            found.AddRange(unlinked.Select(connector => (connector.Name, connector.Edid, (int?)null)));
        }

        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        return [.. found
            .OrderBy(display => display.Name, StringComparer.Ordinal)
            .Select(display => new LinuxDisplay(Describe(display.Edid, keys), display.Name, display.Bus))];
    }

    private static DisplayDescriptor Describe(byte[] edidBytes, Dictionary<string, int> keys)
    {
        var edid = Edid.Parse(edidBytes)!;
        var identity = "edid-" + Edid.IdentityOf(edidBytes);
        var occurrence = keys[identity] = keys.GetValueOrDefault(identity) + 1;
        var key = occurrence == 1 ? identity : $"{identity}-{occurrence.ToString(CultureInfo.InvariantCulture)}";
        return new DisplayDescriptor(key, edid.ProductName ?? AppText.UnknownDisplay, edid.ManufacturerCode);
    }
}
