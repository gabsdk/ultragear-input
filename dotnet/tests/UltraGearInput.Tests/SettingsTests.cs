using UltraGearInput.Core;

namespace UltraGearInput.Tests;

public sealed class SettingsTests
{
    private static readonly DisplayDescriptor LgDisplay = new("display-a", "LG ULTRAGEAR", "GSM");
    private static readonly DisplayDescriptor OtherDisplay = new("display-b", "DELL U2723QE", "DEL");

    [Fact]
    public void MaximumInputsMatchesSpec() => Assert.Equal(Settings.MaximumInputs, ProtocolSpec.Shared.MaximumInputs);

    [Fact]
    public void DecodesSpecCases()
    {
        foreach (var settingsCase in ProtocolSpec.Shared.SettingsCases)
        {
            var error = Record.Exception(() => Settings.Decode(settingsCase.Json));
            if (settingsCase.Error is null)
            {
                Assert.Null(error);
                continue;
            }
            var settingsError = Assert.IsType<SettingsException>(error);
            Assert.Equal(settingsCase.Error, ToSpecName(settingsError.Error));
        }
    }

    [Fact]
    public void EmptySettingsUseDefaults()
    {
        var settings = Settings.Decode("{}");
        Assert.Equal(InputSource.Defaults, settings.ResolvedInputs);
        Assert.Equal(DdcProtocol.Lg, settings.ProtocolFor(LgDisplay));
        Assert.Equal(DdcProtocol.Standard, settings.ProtocolFor(OtherDisplay));
    }

    [Fact]
    public void OverrideWinsOverDefault()
    {
        var settings = new Settings { Protocols = new Dictionary<string, DdcProtocol> { ["display-a"] = DdcProtocol.Standard } };
        Assert.Equal(DdcProtocol.Standard, settings.ProtocolFor(LgDisplay));
    }

    [Fact]
    public void SettingDefaultProtocolRemovesOverride()
    {
        var overridden = new Settings().WithProtocol(DdcProtocol.Standard, LgDisplay);
        Assert.Equal(DdcProtocol.Standard, Assert.Single(overridden.Protocols).Value);
        Assert.Empty(overridden.WithProtocol(DdcProtocol.Lg, LgDisplay).Protocols);
    }

    [Fact]
    public void RoundTripsThroughEncoding()
    {
        var settings = new Settings
        {
            Inputs = [new InputSource("dp", "DisplayPort", 15, 208)],
            Protocols = new Dictionary<string, DdcProtocol> { ["display-b"] = DdcProtocol.Lg },
        };
        var decoded = Settings.Decode(settings.Encode());
        Assert.Equal(settings.Inputs, decoded.Inputs);
        Assert.Equal(settings.Protocols, decoded.Protocols);
    }

    [Fact]
    public void EncodesProtocolsAsLowercaseNames()
    {
        var json = new Settings().WithProtocol(DdcProtocol.Standard, LgDisplay).Encode();
        Assert.Contains("\"display-a\": \"standard\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsIntegerProtocolValues() =>
        Assert.Equal(SettingsError.Malformed, Assert.Throws<SettingsException>(() => Settings.Decode("""{"protocols":{"a":1}}""")).Error);

    [Fact]
    public void RejectsMissingInputFields() =>
        Assert.Equal(SettingsError.Malformed, Assert.Throws<SettingsException>(() => Settings.Decode("""{"inputs":[{"id":"dp","name":"DP","standardValue":15}]}""")).Error);

    [Fact]
    public void DisplayReportUsesResolvedProtocol()
    {
        var json = DisplayReport.Encode([DisplayReport.From(LgDisplay, new Settings())]);
        Assert.Contains("\"protocol\": \"lg\"", json, StringComparison.Ordinal);
        Assert.Contains("\"manufacturerCode\": \"GSM\"", json, StringComparison.Ordinal);
    }

    private static string ToSpecName(SettingsError error)
    {
        var name = error.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
