using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraGearInput.Core;

public enum SettingsError
{
    Malformed,
    EmptyInputs,
    TooManyInputs,
    BlankInputId,
    DuplicateInputId,
    BlankInputName,
}

public sealed class SettingsException(SettingsError error) : Exception(error.ToString())
{
    public SettingsError Error { get; } = error;
}

public sealed record Settings
{
    public const int MaximumInputs = 32;

    private static readonly IReadOnlyDictionary<string, DdcProtocol> NoProtocols = new Dictionary<string, DdcProtocol>();

    public IReadOnlyList<InputSource>? Inputs { get; init; }

    public IReadOnlyDictionary<string, DdcProtocol> Protocols { get; init => field = value ?? NoProtocols; } = NoProtocols;

    [JsonIgnore]
    public IReadOnlyList<InputSource> ResolvedInputs => Inputs ?? InputSource.Defaults;

    public DdcProtocol ProtocolFor(DisplayDescriptor display) =>
        Protocols.TryGetValue(display.Key, out var ddcProtocol) ? ddcProtocol : display.DefaultProtocol;

    public Settings WithProtocol(DdcProtocol ddcProtocol, DisplayDescriptor display)
    {
        var protocols = new Dictionary<string, DdcProtocol>(Protocols, StringComparer.Ordinal);
        if (ddcProtocol == display.DefaultProtocol)
        {
            protocols.Remove(display.Key);
        }
        else
        {
            protocols[display.Key] = ddcProtocol;
        }
        return this with { Protocols = protocols };
    }

    public static Settings Decode(string json)
    {
        Settings? settings;
        try
        {
            settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.Settings);
        }
        catch (JsonException)
        {
            throw new SettingsException(SettingsError.Malformed);
        }
        if (settings is null)
        {
            throw new SettingsException(SettingsError.Malformed);
        }
        settings.Validate();
        return settings;
    }

    public string Encode() => JsonSerializer.Serialize(this, SettingsJsonContext.Default.Settings);

    public void Validate()
    {
        if (Inputs is null)
        {
            return;
        }
        var error = Inputs switch
        {
            { Count: 0 } => SettingsError.EmptyInputs,
            { Count: > MaximumInputs } => SettingsError.TooManyInputs,
            _ when Inputs.Any(input => string.IsNullOrWhiteSpace(input.Id)) => SettingsError.BlankInputId,
            _ when Inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count() != Inputs.Count => SettingsError.DuplicateInputId,
            _ when Inputs.Any(input => string.IsNullOrWhiteSpace(input.Name)) => SettingsError.BlankInputName,
            _ => (SettingsError?)null,
        };
        if (error is { } value)
        {
            throw new SettingsException(value);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(IReadOnlyList<DisplayReport>))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;

public sealed record DisplayReport(string Key, string Name, string? ManufacturerCode, DdcProtocol Protocol)
{
    public static DisplayReport From(DisplayDescriptor display, Settings settings) =>
        new(display.Key, display.Name, display.ManufacturerCode, settings.ProtocolFor(display));

    public static string Encode(IReadOnlyList<DisplayReport> reports) =>
        JsonSerializer.Serialize(reports, SettingsJsonContext.Default.IReadOnlyListDisplayReport);
}
