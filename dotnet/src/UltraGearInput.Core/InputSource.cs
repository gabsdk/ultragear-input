namespace UltraGearInput.Core;

public sealed record InputSource(string Id, string Name, ushort StandardValue, ushort LgValue)
{
    public static IReadOnlyList<InputSource> Defaults { get; } =
    [
        new("displayPort1", "DisplayPort 1", 0x0F, 0xD0),
        new("displayPort2", "DisplayPort 2 / USB-C", 0x10, 0xD1),
        new("usbC", "USB-C / DisplayPort 3", 0x1B, 0xD2),
        new("hdmi1", "HDMI 1", 0x11, 0x90),
        new("hdmi2", "HDMI 2", 0x12, 0x91),
    ];

    public ushort ValueFor(DdcProtocol ddcProtocol) => ddcProtocol == DdcProtocol.Lg ? LgValue : StandardValue;
}
