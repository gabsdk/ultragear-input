using System.Text;

namespace UltraGearInput.Linux.DBus;

internal sealed record DBusEndpoint(string Path, bool IsAbstract)
{
    public string SocketName => IsAbstract ? "\0" + Path : Path;
}

internal static class DBusAddress
{
    public const string SessionVariable = "DBUS_SESSION_BUS_ADDRESS";
    public const string RuntimeDirectoryVariable = "XDG_RUNTIME_DIR";

    public static DBusEndpoint? Session(Func<string, string?> environment)
    {
        var address = environment(SessionVariable);
        if (!string.IsNullOrWhiteSpace(address))
        {
            return Parse(address);
        }
        var runtimeDirectory = environment(RuntimeDirectoryVariable);
        return string.IsNullOrWhiteSpace(runtimeDirectory)
            ? null
            : new DBusEndpoint(System.IO.Path.Combine(runtimeDirectory, "bus"), IsAbstract: false);
    }

    public static DBusEndpoint? Parse(string address)
    {
        foreach (var entry in address.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = entry.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0 || entry[..colon] != "unix")
            {
                continue;
            }
            var options = entry[(colon + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(option => option.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .ToDictionary(pair => pair[0], pair => Unescape(pair[1]), StringComparer.Ordinal);
            if (options.TryGetValue("path", out var path))
            {
                return new DBusEndpoint(path, IsAbstract: false);
            }
            if (options.TryGetValue("abstract", out var name))
            {
                return new DBusEndpoint(name, IsAbstract: true);
            }
        }
        return null;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('%', StringComparison.Ordinal))
        {
            return value;
        }
        var bytes = new List<byte>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' && index + 2 < value.Length)
            {
                bytes.Add(Convert.ToByte(value.Substring(index + 1, 2), 16));
                index += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[index].ToString()));
            }
        }
        return Encoding.UTF8.GetString([.. bytes]);
    }
}
