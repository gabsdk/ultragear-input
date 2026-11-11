using System.Text.RegularExpressions;

namespace UltraGearInput.Core;

public sealed record DisplayDescriptor(string Key, string Name, string? ManufacturerCode)
{
    public bool IsLg =>
        ManufacturerCode == Core.ManufacturerCode.Lg
        || Name.StartsWith("LG", StringComparison.OrdinalIgnoreCase);

    public DdcProtocol DefaultProtocol => IsLg ? DdcProtocol.Lg : DdcProtocol.Standard;
}

public static partial class ManufacturerCode
{
    public const string Lg = "GSM";

    public static string? Decode(ushort raw)
    {
        if ((raw & 0x8000) != 0)
        {
            return null;
        }
        int[] letters = [(raw >> 10) & 0x1F, (raw >> 5) & 0x1F, raw & 0x1F];
        return letters.All(letter => letter is >= 1 and <= 26)
            ? string.Concat(letters.Select(letter => (char)('@' + letter)))
            : null;
    }

    public static string? FromDevicePath(string devicePath)
    {
        var match = DevicePathPattern().Match(devicePath);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"DISPLAY#([A-Z]{3})[0-9A-F]{4}#", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DevicePathPattern();
}
