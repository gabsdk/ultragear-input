using UltraGearInput.Core;
using UltraGearInput.Linux.Hardware;

namespace UltraGearInput.Tests.Linux;

public sealed class SysfsDisplayTests : IDisposable
{
    private readonly TemporaryDirectory sysfs = new();
    private readonly byte[] lg = ProtocolSpec.Shared.Lg.Bytes;
    private readonly byte[] dell = ProtocolSpec.Shared.Dell.Bytes;

    public void Dispose() => sysfs.Dispose();

    [Fact]
    public void FindsConnectorsWithDdcLinksAndAuxChildren()
    {
        var gpu = AddGpu("0000:03:00.0");
        AddConnector(gpu, "card1-HDMI-A-1", lg, ddcBus: 4);
        AddConnector(gpu, "card1-DP-2", dell, auxBus: 7);

        var connectors = Scanner().Connectors();

        Assert.Equal(["card1-DP-2", "card1-HDMI-A-1"], connectors.Select(connector => connector.Name));
        Assert.Equal([7, 4], connectors.Select(connector => connector.Bus));
        Assert.Equal(lg, connectors[1].Edid);
    }

    [Fact]
    public void SkipsInternalAndDisconnectedConnectors()
    {
        var gpu = AddGpu("0000:00:02.0");
        AddConnector(gpu, "card0-eDP-1", lg, ddcBus: 1);
        AddConnector(gpu, "card0-DP-1", dell, ddcBus: 2, status: "disconnected");
        AddConnector(gpu, "card0-Writeback-1", [], status: "unknown");

        var connectors = Scanner().Connectors();
        Assert.Equal(["card0-DP-1", "card0-Writeback-1"], connectors.Select(connector => connector.Name));
        Assert.All(connectors, connector => Assert.False(connector.IsConnected));
        Assert.Empty(DisplayResolver.Resolve(connectors, [1, 2], _ => ProtocolSpec.Shared.Lg.Bytes));
    }

    [Fact]
    public void FindsOnlyAdaptersThatBelongToADisplayController()
    {
        var gpu = AddGpu("0000:01:00.0");
        AddAdapter(gpu, 3);
        AddAdapter(sysfs.CreateDirectory("devices", "pci0000:00", "0000:00:1f.4"), 0, pciClass: "0x0c0500");
        AddAdapter(sysfs.CreateDirectory("devices", "platform", "soc"), 9);

        Assert.Equal([3], Scanner().DisplayAdapterBuses());
    }

    [Fact]
    public void ResolvesLinkedConnectorsWithoutProbing()
    {
        var probed = new List<int>();
        var displays = DisplayResolver.Resolve(
            [new DrmConnector("card1-HDMI-A-1", lg, 4)],
            [4, 5],
            bus =>
            {
                probed.Add(bus);
                return null;
            });

        var display = Assert.Single(displays);
        Assert.Empty(probed);
        Assert.Equal(("card1-HDMI-A-1", 4), (display.Connector, display.Bus!.Value));
        Assert.Equal(new DisplayDescriptor("edid-" + ProtocolSpec.Shared.Lg.Identity, "LG ULTRAGEAR", "GSM"), display.Descriptor);
        Assert.Equal(DdcProtocol.Lg, display.Descriptor.DefaultProtocol);
    }

    [Fact]
    public void MatchesUnlinkedConnectorsByProbingAdapterEdids()
    {
        var displays = DisplayResolver.Resolve(
            [new DrmConnector("card1-DP-1", lg, null), new DrmConnector("card1-DP-2", dell, null)],
            [6, 7, 8],
            bus => bus switch { 7 => dell, 8 => lg, _ => null });

        Assert.Equal([("card1-DP-1", 8), ("card1-DP-2", 7)], displays.Select(display => (display.Connector, display.Bus!.Value)));
    }

    [Fact]
    public void KeepsDisplaysWithoutAChannelSoTheMenuCanExplain()
    {
        var display = Assert.Single(DisplayResolver.Resolve([new DrmConnector("card0-DP-1", lg, null)], [], _ => null));
        Assert.Null(display.Bus);
    }

    [Fact]
    public void ProbesEveryAdapterWhenThereAreNoDrmConnectors()
    {
        var displays = DisplayResolver.Resolve([], [2, 3], bus => bus == 3 ? lg : null);
        var display = Assert.Single(displays);
        Assert.Equal(("i2c-3", 3), (display.Connector, display.Bus!.Value));
    }

    [Fact]
    public void IgnoresConnectorsWithoutAValidEdid()
    {
        var displays = DisplayResolver.Resolve([new DrmConnector("card0-DP-1", [1, 2, 3], 1)], [], _ => null);
        Assert.Empty(displays);
    }

    [Fact]
    public void GivesIdenticalMonitorsDistinctKeys()
    {
        var displays = DisplayResolver.Resolve([new DrmConnector("card0-DP-1", lg, 1), new DrmConnector("card0-DP-2", lg, 2)], [], _ => null);
        var identity = "edid-" + ProtocolSpec.Shared.Lg.Identity;
        Assert.Equal([identity, identity + "-2"], displays.Select(display => display.Descriptor.Key));
    }

    [Fact]
    public void InventoryCachesProbesUntilTheConnectorsChange()
    {
        var gpu = AddGpu("0000:01:00.0");
        AddAdapter(gpu, 3);
        AddConnector(gpu, "card0-DP-1", lg);
        var probes = 0;
        var inventory = new DisplayInventory(Scanner(), _ =>
        {
            probes++;
            return lg;
        });

        Assert.Equal(3, Assert.Single(inventory.Scan()).Bus);
        inventory.Scan();
        Assert.Equal(1, probes);

        File.WriteAllText(System.IO.Path.Combine(gpu, "drm", "card0", "card0-DP-1", "status"), "disconnected\n");
        Assert.Empty(inventory.Scan());
        File.WriteAllText(System.IO.Path.Combine(gpu, "drm", "card0", "card0-DP-1", "status"), "connected\n");
        inventory.Scan();
        Assert.Equal(2, probes);
    }

    [Fact]
    public void ScannerToleratesAMissingSysfs()
    {
        var scanner = new SysfsScanner(sysfs.Combine("missing"));
        Assert.Empty(scanner.Connectors());
        Assert.Empty(scanner.DisplayAdapterBuses());
    }

    [Theory]
    [InlineData("i2c-12", 12)]
    [InlineData("i2c-x", null)]
    [InlineData("drm_dp_aux0", null)]
    public void ParsesBusNames(string name, int? bus) => Assert.Equal(bus, SysfsScanner.BusNumber(name));

    private SysfsScanner Scanner() => new(sysfs.Path);

    private string AddGpu(string address)
    {
        var device = sysfs.CreateDirectory("devices", "pci0000:00", address);
        File.WriteAllText(System.IO.Path.Combine(device, "class"), "0x030000\n");
        return device;
    }

    private void AddConnector(string gpu, string name, byte[] edid, int? ddcBus = null, int? auxBus = null, string status = "connected")
    {
        var card = name[..name.IndexOf('-', StringComparison.Ordinal)];
        var connector = Directory.CreateDirectory(System.IO.Path.Combine(gpu, "drm", card, name)).FullName;
        File.WriteAllText(System.IO.Path.Combine(connector, "status"), status + "\n");
        File.WriteAllBytes(System.IO.Path.Combine(connector, "edid"), edid);
        if (ddcBus is { } bus)
        {
            var adapter = Directory.CreateDirectory(System.IO.Path.Combine(gpu, $"i2c-{bus}")).FullName;
            File.CreateSymbolicLink(System.IO.Path.Combine(connector, "ddc"), System.IO.Path.GetRelativePath(connector, adapter));
        }
        if (auxBus is { } aux)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(connector, $"i2c-{aux}"));
        }
        sysfs.Link(System.IO.Path.GetRelativePath(sysfs.Combine("class", "drm"), connector), "class", "drm", name);
    }

    private void AddAdapter(string parent, int bus, string? pciClass = null)
    {
        if (pciClass is not null)
        {
            File.WriteAllText(System.IO.Path.Combine(parent, "class"), pciClass + "\n");
        }
        var adapter = Directory.CreateDirectory(System.IO.Path.Combine(parent, $"i2c-{bus}")).FullName;
        var node = Directory.CreateDirectory(System.IO.Path.Combine(adapter, "i2c-dev", $"i2c-{bus}")).FullName;
        File.CreateSymbolicLink(System.IO.Path.Combine(node, "device"), "../..");
        sysfs.Link(System.IO.Path.GetRelativePath(sysfs.Combine("class", "i2c-dev"), node), "class", "i2c-dev", $"i2c-{bus}");
    }
}
