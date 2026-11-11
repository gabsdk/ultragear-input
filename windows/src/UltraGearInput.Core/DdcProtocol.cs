using System.Text.Json.Serialization;

namespace UltraGearInput.Core;

[JsonConverter(typeof(DdcProtocolJsonConverter))]
public enum DdcProtocol
{
    [JsonStringEnumMemberName("standard")]
    Standard,

    [JsonStringEnumMemberName("lg")]
    Lg,
}

public static class DdcProtocolExtensions
{
    extension(DdcProtocol ddcProtocol)
    {
        public byte SourceAddress => ddcProtocol switch
        {
            DdcProtocol.Lg => 0x50,
            _ => 0x51,
        };

        public byte InputCode => ddcProtocol switch
        {
            DdcProtocol.Lg => 0xF4,
            _ => 0x60,
        };
    }
}

public sealed class DdcProtocolJsonConverter() : JsonStringEnumConverter<DdcProtocol>(namingPolicy: null, allowIntegerValues: false);
