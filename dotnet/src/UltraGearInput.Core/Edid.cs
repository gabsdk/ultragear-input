using System.Buffers.Binary;
using System.Text;

namespace UltraGearInput.Core;

public sealed record Edid(string? ManufacturerCode, ushort ProductCode, uint SerialNumber, string? ProductName)
{
    public const int BlockLength = 128;

    private const int ManufacturerOffset = 8;
    private const int IdentityLength = 10;
    private const int FirstDescriptorOffset = 54;
    private const int DescriptorLength = 18;
    private const int DescriptorCount = 4;
    private const byte ProductNameTag = 0xFC;

    private static ReadOnlySpan<byte> Header => [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    public static bool IsValid(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= BlockLength && bytes[..Header.Length].SequenceEqual(Header);

    public static Edid? Parse(ReadOnlySpan<byte> bytes)
    {
        if (!IsValid(bytes))
        {
            return null;
        }
        return new Edid(
            Core.ManufacturerCode.Decode(BinaryPrimitives.ReadUInt16BigEndian(bytes[ManufacturerOffset..])),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[(ManufacturerOffset + 2)..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[(ManufacturerOffset + 4)..]),
            ProductNameOf(bytes));
    }

    public static string IdentityOf(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(bytes.Slice(ManufacturerOffset, IdentityLength));

    public static bool SameBaseBlock(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        IsValid(left) && IsValid(right) && left[..BlockLength].SequenceEqual(right[..BlockLength]);

    private static string? ProductNameOf(ReadOnlySpan<byte> bytes)
    {
        for (var index = 0; index < DescriptorCount; index++)
        {
            var descriptor = bytes.Slice(FirstDescriptorOffset + (index * DescriptorLength), DescriptorLength);
            if (descriptor[0] == 0 && descriptor[1] == 0 && descriptor[2] == 0 && descriptor[3] == ProductNameTag)
            {
                var text = descriptor[5..];
                var end = text.IndexOf((byte)0x0A);
                var name = Encoding.ASCII.GetString(end < 0 ? text : text[..end]).Trim();
                return name.Length > 0 ? name : null;
            }
        }
        return null;
    }
}
