using UltraGearInput.Core;

namespace UltraGearInput.Tests;

public sealed class DisplayDescriptorTests
{
    [Fact]
    public void DecodesManufacturerVectors()
    {
        foreach (var vector in ProtocolSpec.Shared.Manufacturers)
        {
            Assert.Equal(vector.Code, ManufacturerCode.Decode(vector.Raw));
        }
    }

    [Fact]
    public void RejectsOutOfRangeLetters() => Assert.Null(ManufacturerCode.Decode(0x7FFF));

    [Fact]
    public void ParsesDevicePathVectors()
    {
        foreach (var vector in ProtocolSpec.Shared.DevicePaths)
        {
            Assert.Equal(vector.Code, ManufacturerCode.FromDevicePath(vector.Path));
        }
    }

    [Theory]
    [InlineData("GSM", "Monitor", true)]
    [InlineData(null, "LG ULTRAGEAR", true)]
    [InlineData(null, "lg hdr 4k", true)]
    [InlineData("DEL", "DELL U2723QE", false)]
    [InlineData(null, "External Display", false)]
    public void DetectsLgDisplays(string? code, string name, bool expected)
    {
        var display = new DisplayDescriptor("key", name, code);
        Assert.Equal(expected, display.IsLg);
        Assert.Equal(expected ? DdcProtocol.Lg : DdcProtocol.Standard, display.DefaultProtocol);
    }

    [Fact]
    public void InputDefaultsMatchSpec() => Assert.Equal(ProtocolSpec.Shared.DefaultInputs, InputSource.Defaults);

    [Fact]
    public void InputValueDependsOnProtocol()
    {
        var input = new InputSource("hdmi1", "HDMI 1", 17, 144);
        Assert.Equal(17, input.ValueFor(DdcProtocol.Standard));
        Assert.Equal(144, input.ValueFor(DdcProtocol.Lg));
    }
}
