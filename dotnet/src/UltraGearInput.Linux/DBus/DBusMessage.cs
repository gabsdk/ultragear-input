using System.Buffers.Binary;

namespace UltraGearInput.Linux.DBus;

internal enum DBusMessageType : byte
{
    MethodCall = 1,
    MethodReturn = 2,
    Error = 3,
    Signal = 4,
}

[Flags]
internal enum DBusMessageFlags : byte
{
    None = 0,
    NoReplyExpected = 1,
    NoAutoStart = 2,
}

internal sealed record DBusMessage(DBusMessageType Type)
{
    public const int FixedHeaderLength = 16;
    public const int MaximumLength = 64 * 1024 * 1024;

    private const byte LittleEndian = (byte)'l';
    private const byte BigEndian = (byte)'B';
    private const byte ProtocolVersion = 1;

    private enum Field : byte
    {
        Path = 1,
        Interface = 2,
        Member = 3,
        ErrorName = 4,
        ReplySerial = 5,
        Destination = 6,
        Sender = 7,
        Signature = 8,
    }

    public DBusMessageFlags Flags { get; init; }

    public uint Serial { get; init; }

    public string? Path { get; init; }

    public string? Interface { get; init; }

    public string? Member { get; init; }

    public string? ErrorName { get; init; }

    public uint? ReplySerial { get; init; }

    public string? Destination { get; init; }

    public string? Sender { get; init; }

    public string Signature { get; init; } = "";

    public IReadOnlyList<object> Body { get; init; } = [];

    public bool ExpectsReply => Type == DBusMessageType.MethodCall && !Flags.HasFlag(DBusMessageFlags.NoReplyExpected);

    public static DBusMessage MethodCall(string destination, string path, string @interface, string member, string signature = "", params object[] body) =>
        new(DBusMessageType.MethodCall)
        {
            Destination = destination,
            Path = path,
            Interface = @interface,
            Member = member,
            Signature = signature,
            Body = body,
        };

    public static DBusMessage Signal(string path, string @interface, string member, string signature = "", params object[] body) =>
        new(DBusMessageType.Signal)
        {
            Path = path,
            Interface = @interface,
            Member = member,
            Signature = signature,
            Body = body,
        };

    public DBusMessage Reply(string signature = "", params object[] body) =>
        new(DBusMessageType.MethodReturn)
        {
            ReplySerial = Serial,
            Destination = Sender,
            Signature = signature,
            Body = body,
        };

    public DBusMessage ErrorReply(string name, string message) =>
        new(DBusMessageType.Error)
        {
            ReplySerial = Serial,
            Destination = Sender,
            ErrorName = name,
            Signature = "s",
            Body = [message],
        };

    public byte[] Encode(uint serial)
    {
        var body = new DBusWriter();
        body.Write(Signature, Body);

        var message = new DBusWriter();
        message.WriteRaw([LittleEndian, (byte)Type, (byte)Flags, ProtocolVersion]);
        message.WriteValue("u", (uint)body.Length);
        message.WriteValue("u", serial);
        message.WriteValue("a(yv)", HeaderFields());
        message.Align(8);
        message.WriteRaw(body.ToArray());
        return message.ToArray();
    }

    public static int? FrameLength(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < FixedHeaderLength)
        {
            return null;
        }
        var bigEndian = buffer[0] switch
        {
            LittleEndian => false,
            BigEndian => true,
            _ => throw new FormatException("Unknown D-Bus endianness marker."),
        };
        var bodyLength = ReadUInt32(buffer[4..], bigEndian);
        var fieldsLength = ReadUInt32(buffer[12..], bigEndian);
        var headerLength = (FixedHeaderLength + fieldsLength + 7) / 8 * 8;
        var total = headerLength + bodyLength;
        return total > MaximumLength
            ? throw new FormatException($"D-Bus message of {total} bytes exceeds the limit.")
            : (int)total;
    }

    public static DBusMessage Decode(ReadOnlyMemory<byte> frame)
    {
        var span = frame.Span;
        var bigEndian = span[0] == BigEndian;
        if (span[3] != ProtocolVersion)
        {
            throw new FormatException($"Unsupported D-Bus protocol version {span[3]}.");
        }
        var reader = new DBusReader(frame, bigEndian, 8);
        var serial = reader.ReadUInt32();
        var fields = (IReadOnlyList<object>)reader.ReadValue("a(yv)");
        reader.Align(8);

        var message = new DBusMessage((DBusMessageType)span[1]) { Flags = (DBusMessageFlags)span[2], Serial = serial };
        foreach (var field in fields.Cast<object[]>())
        {
            message = Apply(message, (Field)(byte)field[0], ((DBusVariant)field[1]).Value);
        }
        var body = message.Signature.Length == 0 ? [] : new DBusReader(frame, bigEndian, reader.Position).Read(message.Signature);
        return message with { Body = body };
    }

    private static DBusMessage Apply(DBusMessage message, Field field, object value) => field switch
    {
        Field.Path => message with { Path = ((DBusObjectPath)value).Value },
        Field.Interface => message with { Interface = (string)value },
        Field.Member => message with { Member = (string)value },
        Field.ErrorName => message with { ErrorName = (string)value },
        Field.ReplySerial => message with { ReplySerial = (uint)value },
        Field.Destination => message with { Destination = (string)value },
        Field.Sender => message with { Sender = (string)value },
        Field.Signature => message with { Signature = ((DBusSignature)value).Value },
        _ => message,
    };

    private List<object> HeaderFields()
    {
        var fields = new List<object>();
        void Add(Field field, string signature, object? value)
        {
            if (value is not null)
            {
                fields.Add(new object[] { (byte)field, new DBusVariant(signature, value) });
            }
        }
        Add(Field.Path, "o", Path is null ? null : new DBusObjectPath(Path));
        Add(Field.Interface, "s", Interface);
        Add(Field.Member, "s", Member);
        Add(Field.ErrorName, "s", ErrorName);
        Add(Field.ReplySerial, "u", ReplySerial);
        Add(Field.Destination, "s", Destination);
        Add(Field.Sender, "s", Sender);
        Add(Field.Signature, "g", Signature.Length == 0 ? null : new DBusSignature(Signature));
        return fields;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> span, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
}
