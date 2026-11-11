using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltraGearInput.Linux.Hardware;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct I2cSmbusIoctlData
{
    public byte ReadWrite;
    public byte Command;
    public uint Size;
    public byte* Data;
}

internal static class Errno
{
    public const int NoPermission = 1;
    public const int NoEntry = 2;
    public const int NoDevice = 6;
    public const int AccessDenied = 13;
    public const int Busy = 16;
    public const int NotTty = 25;
    public const int RemoteIo = 121;
}

[SupportedOSPlatform("linux")]
internal static unsafe partial class LibC
{
    public const int ReadWrite = 0x2;
    public const int CloseOnExec = 0x80000;

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    public static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    public static partial nint Read(int descriptor, byte* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    public static partial nint Write(int descriptor, byte* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static partial int IoctlValue(int descriptor, nuint request, nuint argument);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static partial int IoctlPointer(int descriptor, nuint request, void* argument);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    public static partial uint GetEffectiveUserId();
}
