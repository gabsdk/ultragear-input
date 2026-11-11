using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace UltraGearInput.Linux.DBus;

internal sealed class DBusWriter
{
    private readonly List<byte> bytes = [];

    public int Length => bytes.Count;

    public byte[] ToArray() => [.. bytes];

    public void Align(int alignment)
    {
        while (bytes.Count % alignment != 0)
        {
            bytes.Add(0);
        }
    }

    public void WriteRaw(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            bytes.Add(value);
        }
    }

    public void Write(string signature, IReadOnlyList<object> values)
    {
        var types = SignatureParser.Split(signature);
        if (types.Count != values.Count)
        {
            throw new ArgumentException($"Signature '{signature}' expects {types.Count} values, got {values.Count}.", nameof(values));
        }
        for (var index = 0; index < types.Count; index++)
        {
            WriteValue(types[index], values[index]);
        }
    }

    public void WriteValue(string type, object value)
    {
        switch (type[0])
        {
            case 'y':
                bytes.Add(Expect<byte>(type, value));
                break;
            case 'b':
                WriteUInt32(Expect<bool>(type, value) ? 1u : 0u);
                break;
            case 'n':
                WriteFixed(2, buffer => BinaryPrimitives.WriteInt16LittleEndian(buffer, Expect<short>(type, value)));
                break;
            case 'q':
                WriteFixed(2, buffer => BinaryPrimitives.WriteUInt16LittleEndian(buffer, Expect<ushort>(type, value)));
                break;
            case 'i':
                WriteFixed(4, buffer => BinaryPrimitives.WriteInt32LittleEndian(buffer, Expect<int>(type, value)));
                break;
            case 'u':
            case 'h':
                WriteUInt32(Expect<uint>(type, value));
                break;
            case 'x':
                WriteFixed(8, buffer => BinaryPrimitives.WriteInt64LittleEndian(buffer, Expect<long>(type, value)));
                break;
            case 't':
                WriteFixed(8, buffer => BinaryPrimitives.WriteUInt64LittleEndian(buffer, Expect<ulong>(type, value)));
                break;
            case 'd':
                WriteFixed(8, buffer => BinaryPrimitives.WriteDoubleLittleEndian(buffer, Expect<double>(type, value)));
                break;
            case 's':
                WriteString(Expect<string>(type, value));
                break;
            case 'o':
                WriteString(Expect<DBusObjectPath>(type, value).Value);
                break;
            case 'g':
                WriteSignature(Expect<DBusSignature>(type, value).Value);
                break;
            case 'v':
                var variant = Expect<DBusVariant>(type, value);
                if (SignatureParser.Split(variant.Signature).Count != 1)
                {
                    throw new ArgumentException($"Variant signature '{variant.Signature}' must be one complete type.", nameof(value));
                }
                WriteSignature(variant.Signature);
                WriteValue(variant.Signature, variant.Value);
                break;
            case 'a':
                WriteArray(type, value);
                break;
            case '(':
                WriteStruct(type, Expect<IReadOnlyList<object>>(type, value));
                break;
            case '{':
                var entry = Expect<DBusDictEntry>(type, value);
                WriteStruct(type, [entry.Key, entry.Value]);
                break;
            default:
                throw new FormatException($"Unsupported type '{type}'.");
        }
    }

    private void WriteArray(string type, object value)
    {
        var elementType = type[1..];
        Align(4);
        var lengthPosition = bytes.Count;
        WriteUInt32(0);
        Align(SignatureParser.Alignment(elementType[0]));
        var start = bytes.Count;
        if (elementType == "y" && value is byte[] raw)
        {
            WriteRaw(raw);
        }
        else
        {
            foreach (var element in Expect<IEnumerable>(type, value))
            {
                WriteValue(elementType, element);
            }
        }
        var length = (uint)(bytes.Count - start);
        Span<byte> encoded = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(encoded, length);
        for (var index = 0; index < 4; index++)
        {
            bytes[lengthPosition + index] = encoded[index];
        }
    }

    private void WriteStruct(string type, IReadOnlyList<object> values)
    {
        var members = SignatureParser.Members(type);
        if (members.Count != values.Count)
        {
            throw new ArgumentException($"Struct '{type}' expects {members.Count} values, got {values.Count}.", nameof(values));
        }
        Align(8);
        for (var index = 0; index < members.Count; index++)
        {
            WriteValue(members[index], values[index]);
        }
    }

    private void WriteUInt32(uint value) =>
        WriteFixed(4, buffer => BinaryPrimitives.WriteUInt32LittleEndian(buffer, value));

    private void WriteString(string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        if (Array.IndexOf(encoded, (byte)0) >= 0)
        {
            throw new ArgumentException("D-Bus strings cannot contain NUL.", nameof(value));
        }
        WriteUInt32((uint)encoded.Length);
        WriteRaw(encoded);
        bytes.Add(0);
    }

    private void WriteSignature(string signature)
    {
        SignatureParser.Split(signature);
        bytes.Add((byte)signature.Length);
        WriteRaw(Encoding.ASCII.GetBytes(signature));
        bytes.Add(0);
    }

    private delegate void FixedWriter(Span<byte> buffer);

    private void WriteFixed(int size, FixedWriter write)
    {
        Align(size);
        Span<byte> buffer = stackalloc byte[size];
        write(buffer);
        WriteRaw(buffer);
    }

    private static T Expect<T>(string type, object value) => value is T typed
        ? typed
        : throw new ArgumentException($"Type '{type}' cannot encode a value of type {value?.GetType().Name ?? "null"}.", nameof(value));
}
