using System.Globalization;
using System.Text.RegularExpressions;

namespace UltraGearInput.Linux.Hardware;

internal sealed record DrmConnector(string Name, byte[] Edid, int? Bus, bool IsConnected = true);

internal sealed partial class SysfsScanner(string sysfsRoot)
{
    private const string DisplayControllerClassPrefix = "0x03";

    private static readonly string[] InternalConnectorTypes = ["eDP", "LVDS", "DSI"];

    public IReadOnlyList<DrmConnector> Connectors()
    {
        var drm = Path.Combine(sysfsRoot, "class", "drm");
        if (!Directory.Exists(drm))
        {
            return [];
        }
        return [.. Directory.EnumerateDirectories(drm)
            .Select(directory => (Directory: directory, Match: ConnectorPattern().Match(Path.GetFileName(directory))))
            .Where(item => item.Match.Success && !IsInternal(item.Match.Groups["type"].Value))
            .OrderBy(item => Path.GetFileName(item.Directory), StringComparer.Ordinal)
            .Select(item => Describe(item.Directory))];
    }

    private static DrmConnector Describe(string directory)
    {
        var isConnected = ReadText(Path.Combine(directory, "status")) == "connected";
        return isConnected
            ? new DrmConnector(Path.GetFileName(directory), ReadBytes(Path.Combine(directory, "edid")), LinkedBus(directory))
            : new DrmConnector(Path.GetFileName(directory), [], null, IsConnected: false);
    }

    public IReadOnlyList<int> DisplayAdapterBuses()
    {
        var adapters = Path.Combine(sysfsRoot, "class", "i2c-dev");
        if (!Directory.Exists(adapters))
        {
            return [];
        }
        var root = PathResolver.RealPath(sysfsRoot);
        return [.. Directory.EnumerateDirectories(adapters)
            .Select(directory => (Directory: directory, Bus: BusNumber(Path.GetFileName(directory))))
            .Where(item => item.Bus is not null && BelongsToDisplayController(Path.Combine(item.Directory, "device"), root))
            .Select(item => item.Bus!.Value)
            .Order()];
    }

    public static int? BusNumber(string name)
    {
        var match = BusPattern().Match(name);
        return match.Success ? int.Parse(match.Groups["bus"].Value, CultureInfo.InvariantCulture) : null;
    }

    private static int? LinkedBus(string connectorDirectory)
    {
        var ddc = Path.Combine(connectorDirectory, "ddc");
        if (Directory.Exists(ddc))
        {
            return BusNumber(Path.GetFileName(PathResolver.RealPath(ddc)));
        }
        return Directory.EnumerateDirectories(connectorDirectory)
            .Select(directory => BusNumber(Path.GetFileName(directory)))
            .FirstOrDefault(bus => bus is not null);
    }

    private static bool BelongsToDisplayController(string device, string root)
    {
        if (!Directory.Exists(device))
        {
            return false;
        }
        for (var directory = PathResolver.RealPath(device); directory.StartsWith(root, StringComparison.Ordinal); directory = Path.GetDirectoryName(directory)!)
        {
            if (ReadText(Path.Combine(directory, "class"))?.StartsWith(DisplayControllerClassPrefix, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
            if (directory == root)
            {
                break;
            }
        }
        return false;
    }

    private static bool IsInternal(string type) =>
        InternalConnectorTypes.Any(prefix => type.StartsWith(prefix, StringComparison.Ordinal));

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[] ReadBytes(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"^card\d+-(?<type>[A-Za-z]+)(-[A-Za-z]+)*-\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectorPattern();

    [GeneratedRegex(@"^i2c-(?<bus>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BusPattern();
}
