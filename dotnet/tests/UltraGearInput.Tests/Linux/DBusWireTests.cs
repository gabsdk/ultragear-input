using UltraGearInput.Linux.DBus;

namespace UltraGearInput.Tests.Linux;

public sealed class DBusWireTests
{
    private static readonly byte[] SignalVector =
    [
        0x6C, 0x04, 0x00, 0x01, 0x06, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x37, 0x00, 0x00, 0x00,
        0x01, 0x01, 0x6F, 0x00, 0x02, 0x00, 0x00, 0x00, 0x2F, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x02, 0x01, 0x73, 0x00, 0x03, 0x00, 0x00, 0x00, 0x62, 0x2E, 0x63, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03, 0x01, 0x73, 0x00, 0x01, 0x00, 0x00, 0x00, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x08, 0x01, 0x67, 0x00, 0x01, 0x73, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x78, 0x00,
    ];

    private static readonly byte[] BigEndianReturnVector =
    [
        0x42, 0x02, 0x00, 0x01, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x0F,
        0x05, 0x01, 0x75, 0x00, 0x00, 0x00, 0x00, 0x09, 0x08, 0x01, 0x67, 0x00, 0x01, 0x75, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x07,
    ];

    [Fact]
    public void EncodesSignalExactlyAsTheSpecificationLaysItOut()
    {
        var signal = DBusMessage.Signal("/a", "b.c", "D", "s", "x");
        Assert.Equal(SignalVector, signal.Encode(1));
    }

    [Fact]
    public void DecodesTheSignalVector()
    {
        Assert.Equal(SignalVector.Length, DBusMessage.FrameLength(SignalVector));
        var message = DBusMessage.Decode(SignalVector);
        Assert.Equal(DBusMessageType.Signal, message.Type);
        Assert.Equal((1u, "/a", "b.c", "D", "s"), (message.Serial, message.Path, message.Interface, message.Member, message.Signature));
        Assert.Equal("x", Assert.Single(message.Body));
    }

    [Fact]
    public void DecodesBigEndianMessages()
    {
        Assert.Equal(BigEndianReturnVector.Length, DBusMessage.FrameLength(BigEndianReturnVector));
        var message = DBusMessage.Decode(BigEndianReturnVector);
        Assert.Equal(DBusMessageType.MethodReturn, message.Type);
        Assert.Equal(9u, message.ReplySerial);
        Assert.Equal(7u, Assert.Single(message.Body));
    }

    [Fact]
    public void FrameLengthWaitsForTheFixedHeader() => Assert.Null(DBusMessage.FrameLength(SignalVector.AsSpan(0, 15)));

    [Fact]
    public void FrameLengthRejectsUnknownEndianness() =>
        Assert.Throws<FormatException>(() => DBusMessage.FrameLength([(byte)'x', 1, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0]));

    [Fact]
    public void FrameLengthRejectsOversizedMessages() =>
        Assert.Throws<FormatException>(() => DBusMessage.FrameLength([0x6C, 1, 0, 1, 0xFF, 0xFF, 0xFF, 0x7F, 1, 0, 0, 0, 0, 0, 0, 0]));

    [Fact]
    public void RoundTripsEveryBasicAndContainerType()
    {
        object[] values =
        [
            (byte)7, true, (short)-2, (ushort)3, -4, 5u, -6L, 7UL, 1.5, "text", new DBusObjectPath("/x/y"), new DBusSignature("a{sv}"),
            new DBusVariant("i", 42),
            new byte[] { 1, 2, 3 },
            new List<object> { "a", "b" },
            new List<object> { new DBusDictEntry("key", new DBusVariant("s", "value")) },
            new object[] { 1, new List<object> { new DBusVariant("(ia{sv}av)", new object[] { 2, new List<object>(), new List<object>() }) } },
        ];
        const string signature = "ybnqiuxtdsogvayasa{sv}(iav)";
        var writer = new DBusWriter();
        writer.Write(signature, values);
        var decoded = new DBusReader(writer.ToArray(), bigEndian: false).Read(signature);

        Assert.Equal(values[..13], decoded.Take(13));
        Assert.Equal((byte[])values[13], decoded[13]);
        Assert.Equal(["a", "b"], (IReadOnlyList<object>)decoded[14]);
        var entry = Assert.IsType<DBusDictEntry>(Assert.Single((IReadOnlyList<object>)decoded[15]));
        Assert.Equal(("key", new DBusVariant("s", "value")), ((string)entry.Key, (DBusVariant)entry.Value));
        var structure = (object[])decoded[16];
        var nested = (object[])((DBusVariant)Assert.Single((IReadOnlyList<object>)structure[1])).Value;
        Assert.Equal(2, nested[0]);
    }

    [Fact]
    public void EmptyArraysStillPadToTheirElementAlignment()
    {
        var writer = new DBusWriter();
        writer.Write("ya(ii)", [(byte)1, new List<object>()]);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, writer.ToArray());
    }

    [Fact]
    public void ArrayLengthExcludesLeadingPadding()
    {
        var writer = new DBusWriter();
        writer.Write("a(y)", [new List<object> { new object[] { (byte)9 } }]);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 9 }, writer.ToArray());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("(")]
    [InlineData("()")]
    [InlineData("a{s")]
    [InlineData("z")]
    public void RejectsInvalidSignatures(string signature) =>
        Assert.Throws<FormatException>(() => SignatureParser.Split(signature));

    [Fact]
    public void RejectsValuesThatDoNotMatchTheSignature()
    {
        var writer = new DBusWriter();
        Assert.Throws<ArgumentException>(() => writer.Write("i", ["not a number"]));
        Assert.Throws<ArgumentException>(() => writer.Write("ii", [1]));
        Assert.Throws<ArgumentException>(() => writer.Write("v", [new DBusVariant("ii", 1)]));
        Assert.Throws<ArgumentException>(() => writer.Write("s", ["nul\0inside"]));
    }

    [Fact]
    public void RejectsTruncatedAndMalformedInput()
    {
        Assert.Throws<FormatException>(() => new DBusReader(new byte[] { 5, 0, 0, 0, (byte)'a' }, false).Read("s"));
        Assert.Throws<FormatException>(() => new DBusReader(new byte[] { 1, 0, 0, 0, (byte)'a', 1 }, false).Read("s"));
        Assert.Throws<FormatException>(() => new DBusReader(new byte[] { 2, 0, 0, 0 }, false).Read("b"));
        Assert.Throws<FormatException>(() => new DBusReader(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }, false).Read("ay"));
    }

    [Fact]
    public void RejectsDeeplyNestedVariants()
    {
        object value = 1;
        for (var level = 0; level < 70; level++)
        {
            value = new DBusVariant(level == 0 ? "i" : "v", value);
        }
        var writer = new DBusWriter();
        writer.Write("v", [value]);
        Assert.Throws<FormatException>(() => new DBusReader(writer.ToArray(), false).Read("v"));
    }

    [Fact]
    public void RepliesTargetTheCallerAndCarryTheSerial()
    {
        var call = new DBusMessage(DBusMessageType.MethodCall) { Serial = 12, Sender = ":1.5", Member = "Ping" };
        var reply = DBusMessage.Decode(call.Reply("s", "ok").Encode(3));
        var error = DBusMessage.Decode(call.ErrorReply("a.b.C", "nope").Encode(4));
        Assert.Equal((DBusMessageType.MethodReturn, 12u, ":1.5"), (reply.Type, reply.ReplySerial!.Value, reply.Destination!));
        Assert.Equal((DBusMessageType.Error, "a.b.C", "nope"), (error.Type, error.ErrorName!, (string)error.Body[0]));
    }

    [Fact]
    public void NoReplyFlagMeansNoReplyIsExpected()
    {
        var call = DBusMessage.MethodCall("a.b", "/", "a.b", "C") with { Flags = DBusMessageFlags.NoReplyExpected };
        Assert.False(DBusMessage.Decode(call.Encode(1)).ExpectsReply);
        Assert.True(DBusMessage.Decode(DBusMessage.MethodCall("a.b", "/", "a.b", "C").Encode(1)).ExpectsReply);
    }
}
