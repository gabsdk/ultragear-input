using System.Buffers.Binary;
using System.Text;

namespace UltraGearInput.Linux.DBus;

internal sealed class DBusReader(ReadOnlyMemory<byte> data, bool bigEndian, int position = 0)
{
    public const int MaximumArrayLength = 64 * 1024 * 1024;

    private const int MaximumDepth = 64;

    private int depth;

    public int Position { get; private set; } = position;

    public IReadOnlyList<object> Read(string signature) =>
        [.. SignatureParser.Split(signature).Select(ReadValue)];

    public void Align(int alignment)
    {
        var padded = (Position + alignment - 1) / alignment * alignment;
        Take(padded - Position);
    }

    public object ReadValue(string type)
    {
        if (++depth > MaximumDepth)
        {
            throw new FormatException("D-Bus value nests too deeply.");
        }
        try
        {
            return ReadValueCore(type);
        }
        finally
        {
            depth--;
        }
    }

    public uint ReadUInt32()
    {
        Align(4);
        var span = Take(4);
        return bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    private object ReadValueCore(string type) => type[0] switch
    {
        'y' => Take(1)[0],
        'b' => ReadUInt32() switch
        {
            0 => false,
            1 => true,
            var other => throw new FormatException($"Invalid boolean value {other}."),
        },
        'n' => ReadFixed(2, span => bigEndian ? BinaryPrimitives.ReadInt16BigEndian(span) : BinaryPrimitives.ReadInt16LittleEndian(span)),
        'q' => ReadFixed(2, span => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span)),
        'i' => ReadFixed(4, span => bigEndian ? BinaryPrimitives.ReadInt32BigEndian(span) : BinaryPrimitives.ReadInt32LittleEndian(span)),
        'u' or 'h' => ReadUInt32(),
        'x' => ReadFixed(8, span => bigEndian ? BinaryPrimitives.ReadInt64BigEndian(span) : BinaryPrimitives.ReadInt64LittleEndian(span)),
        't' => ReadFixed(8, span => bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(span) : BinaryPrimitives.ReadUInt64LittleEndian(span)),
        'd' => ReadFixed(8, span => bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(span) : BinaryPrimitives.ReadDoubleLittleEndian(span)),
        's' => ReadString(),
        'o' => new DBusObjectPath(ReadString()),
        'g' => new DBusSignature(ReadSignature()),
        'v' => ReadVariant(),
        'a' => ReadArray(type[1..]),
        '(' => ReadStruct(type),
        '{' => ReadDictEntry(type),
        _ => throw new FormatException($"Unsupported type '{type}'."),
    };

    private DBusVariant ReadVariant()
    {
        var signature = ReadSignature();
        if (SignatureParser.Split(signature).Count != 1)
        {
            throw new FormatException($"Variant signature '{signature}' must be one complete type.");
        }
        return new DBusVariant(signature, ReadValue(signature));
    }

    private object ReadArray(string elementType)
    {
        var length = ReadUInt32();
        if (length > MaximumArrayLength)
        {
            throw new FormatException($"Array of {length} bytes exceeds the D-Bus limit.");
        }
        Align(SignatureParser.Alignment(elementType[0]));
        var end = Position + (int)length;
        if (end > data.Length)
        {
            throw new FormatException("Array runs past the end of the message.");
        }
        if (elementType == "y")
        {
            return Take((int)length).ToArray();
        }
        var items = new List<object>();
        while (Position < end)
        {
            items.Add(ReadValue(elementType));
        }
        if (Position != end)
        {
            throw new FormatException("Array elements do not match the declared length.");
        }
        return items;
    }

    private object[] ReadStruct(string type)
    {
        Align(8);
        return [.. SignatureParser.Members(type).Select(ReadValue)];
    }

    private DBusDictEntry ReadDictEntry(string type)
    {
        var members = ReadStruct(type);
        return new DBusDictEntry(members[0], members[1]);
    }

    private string ReadString()
    {
        var length = ReadUInt32();
        if (length > MaximumArrayLength)
        {
            throw new FormatException($"String of {length} bytes exceeds the D-Bus limit.");
        }
        var text = Encoding.UTF8.GetString(Take((int)length));
        if (Take(1)[0] != 0)
        {
            throw new FormatException("String is not NUL terminated.");
        }
        return text;
    }

    private string ReadSignature()
    {
        var length = Take(1)[0];
        var text = Encoding.ASCII.GetString(Take(length));
        if (Take(1)[0] != 0)
        {
            throw new FormatException("Signature is not NUL terminated.");
        }
        return text;
    }

    private delegate T FixedReader<out T>(ReadOnlySpan<byte> span);

    private object ReadFixed<T>(int size, FixedReader<T> read) where T : notnull
    {
        Align(size);
        return read(Take(size));
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || Position + count > data.Length)
        {
            throw new FormatException("Unexpected end of D-Bus message.");
        }
        var span = data.Span.Slice(Position, count);
        Position += count;
        return span;
    }
}
