namespace UltraGearInput.Core;

public static class DdcFrame
{
    public const byte DestinationAddress = 0x6E;

    public static byte[] SetInput(ushort value, DdcProtocol ddcProtocol)
    {
        byte[] frame =
        [
            ddcProtocol.SourceAddress,
            0x84,
            0x03,
            ddcProtocol.InputCode,
            (byte)(value >> 8),
            (byte)(value & 0xFF),
            0,
        ];
        frame[^1] = Checksum(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        var checksum = DestinationAddress;
        foreach (var value in bytes)
        {
            checksum ^= value;
        }
        return checksum;
    }

    public static byte[] WithDestination(ReadOnlySpan<byte> frame) => [DestinationAddress, .. frame];
}
