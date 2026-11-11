using UltraGearInput.Core;

namespace UltraGearInput.Tests;

public sealed class DdcFrameTests
{
    private static readonly ProtocolSpec Spec = ProtocolSpec.Shared;

    [Fact]
    public void DestinationAddressMatchesSpec() => Assert.Equal(DdcFrame.DestinationAddress, Spec.DestinationAddress);

    [Theory]
    [InlineData(DdcProtocol.Standard, "standard")]
    [InlineData(DdcProtocol.Lg, "lg")]
    public void ProtocolAddressesMatchSpec(DdcProtocol ddcProtocol, string name)
    {
        var entry = Spec.Protocols[name];
        Assert.Equal(entry.SourceAddress, ddcProtocol.SourceAddress);
        Assert.Equal(entry.InputCode, ddcProtocol.InputCode);
    }

    [Fact]
    public void FramesMatchSpecVectors()
    {
        Assert.Equal(Enum.GetValues<DdcProtocol>().Length * InputSource.Defaults.Count, Spec.Frames.Count);
        foreach (var vector in Spec.Frames)
        {
            var input = InputSource.Defaults.Single(candidate => candidate.Id == vector.InputId);
            Assert.Equal(vector.Bytes, DdcFrame.SetInput(input.ValueFor(vector.Protocol), vector.Protocol));
        }
    }

    [Fact]
    public void EncodesHighByteOfValue()
    {
        var frame = DdcFrame.SetInput(0x1234, DdcProtocol.Standard);
        Assert.Equal(0x12, frame[4]);
        Assert.Equal(0x34, frame[5]);
        Assert.Equal(frame[^1], DdcFrame.Checksum(frame.AsSpan(0, frame.Length - 1)));
    }

    [Fact]
    public void ChecksumOfEmptyBodyIsDestinationAddress() =>
        Assert.Equal(DdcFrame.DestinationAddress, DdcFrame.Checksum([]));

    [Fact]
    public void WithDestinationPrefixesTheFrame()
    {
        var frame = DdcFrame.SetInput(0xD0, DdcProtocol.Lg);
        var buffer = DdcFrame.WithDestination(frame);
        Assert.Equal(DdcFrame.DestinationAddress, buffer[0]);
        Assert.Equal(frame, buffer[1..]);
        Assert.Equal(0, buffer.Aggregate(0, (checksum, value) => checksum ^ value));
    }
}
