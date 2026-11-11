using System.Runtime.InteropServices;
using System.Text;

namespace UltraGearInput.Native;

internal static unsafe class NativeText
{
    public static string FromUtf16(char* buffer, int capacity)
    {
        var span = new ReadOnlySpan<char>(buffer, capacity);
        var length = span.IndexOf('\0');
        return new string(length < 0 ? span : span[..length]);
    }

    public static string FromAnsi(byte* buffer, int capacity)
    {
        var span = new ReadOnlySpan<byte>(buffer, capacity);
        var length = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(length < 0 ? span : span[..length]);
    }

    public static nint Export(nint library, string name) =>
        NativeLibrary.TryGetExport(library, name, out var address) ? address : 0;
}

public static class DeviceNames
{
    public static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
