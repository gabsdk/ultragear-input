using System.Text.Json;
using UltraGearInput.Core;

namespace UltraGearInput.Tests;

public sealed record ProtocolEntry(byte SourceAddress, byte InputCode);

public sealed record FrameVector(DdcProtocol Protocol, string InputId, byte[] Bytes);

public sealed record ManufacturerVector(ushort Raw, string? Code);

public sealed record DevicePathVector(string Path, string? Code);

public sealed record SettingsCase(string Json, string? Error);

public sealed record EdidVector(string File, string ManufacturerCode, ushort ProductCode, uint SerialNumber, string ProductName, string Identity)
{
    public byte[] Bytes => System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", File));
}

public sealed record ProtocolSpec(
    byte DestinationAddress,
    IReadOnlyDictionary<string, ProtocolEntry> Protocols,
    IReadOnlyList<InputSource> DefaultInputs,
    IReadOnlyList<FrameVector> Frames,
    IReadOnlyList<ManufacturerVector> Manufacturers,
    IReadOnlyList<DevicePathVector> DevicePaths,
    int MaximumInputs,
    IReadOnlyList<SettingsCase> SettingsCases,
    IReadOnlyList<EdidVector> Edids)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ByteArrayAsNumbersConverter() },
    };

    public EdidVector Lg => Edids.Single(edid => edid.ManufacturerCode == "GSM");

    public EdidVector Dell => Edids.Single(edid => edid.ManufacturerCode == "DEL");

    public static ProtocolSpec Shared { get; } = JsonSerializer.Deserialize<ProtocolSpec>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "protocol.json")),
        Options)!;
}

internal sealed class ByteArrayAsNumbersConverter : System.Text.Json.Serialization.JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        [.. JsonSerializer.Deserialize<int[]>(ref reader)!.Select(value => checked((byte)value))];

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Select(item => (int)item), options);
}
