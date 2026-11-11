using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltraGearInput.Linux.Hardware;

internal sealed class I2cException(string path, int errno) : IOException($"{path}: errno {errno}")
{
    public string DevicePath { get; } = path;

    public int Errno { get; } = errno;
}

[Flags]
internal enum I2cFunctionality : ulong
{
    None = 0,
    PlainI2c = 0x00000001,
    SmbusReadI2cBlock = 0x04000000,
    SmbusWriteI2cBlock = 0x08000000,
}

internal enum I2cWriteMethod
{
    Unsupported,
    PlainWrite,
    SmbusI2cBlock,
}

internal static class I2cTransfer
{
    public const byte DdcAddress = 0x37;
    public const byte EdidAddress = 0x50;
    public const int SmbusBlockMaximum = 32;

    public static I2cWriteMethod WriteMethod(I2cFunctionality functionality) =>
        functionality.HasFlag(I2cFunctionality.PlainI2c) ? I2cWriteMethod.PlainWrite
        : functionality.HasFlag(I2cFunctionality.SmbusWriteI2cBlock) ? I2cWriteMethod.SmbusI2cBlock
        : I2cWriteMethod.Unsupported;

    public static string DevicePath(string deviceRoot, int bus) => Path.Combine(deviceRoot, $"i2c-{bus}");
}

[SupportedOSPlatform("linux")]
internal sealed unsafe class I2cDevice : IDisposable
{
    private const nuint SetSlaveAddress = 0x0703;
    private const nuint GetFunctionality = 0x0705;
    private const nuint SmbusTransfer = 0x0720;
    private const byte SmbusWrite = 0;
    private const byte SmbusRead = 1;
    private const uint SmbusI2cBlockData = 8;
    private const int SmbusDataLength = 34;

    private readonly string path;
    private int descriptor;

    private I2cDevice(string path, int descriptor)
    {
        this.path = path;
        this.descriptor = descriptor;
    }

    public static I2cDevice Open(string path)
    {
        var descriptor = LibC.Open(path, LibC.ReadWrite | LibC.CloseOnExec);
        return descriptor < 0 ? throw new I2cException(path, Marshal.GetLastPInvokeError()) : new I2cDevice(path, descriptor);
    }

    public I2cFunctionality Functionality()
    {
        ulong functionality = 0;
        Check(LibC.IoctlPointer(descriptor, GetFunctionality, &functionality));
        return (I2cFunctionality)functionality;
    }

    public void SelectAddress(byte address) => Check(LibC.IoctlValue(descriptor, SetSlaveAddress, address));

    public void Write(ReadOnlySpan<byte> data)
    {
        fixed (byte* pointer = data)
        {
            var written = LibC.Write(descriptor, pointer, (nuint)data.Length);
            if (written < 0)
            {
                throw new I2cException(path, Marshal.GetLastPInvokeError());
            }
            if (written != data.Length)
            {
                throw new I2cException(path, Errno.RemoteIo);
            }
        }
    }

    public void Read(Span<byte> data)
    {
        fixed (byte* pointer = data)
        {
            var read = LibC.Read(descriptor, pointer, (nuint)data.Length);
            if (read < 0)
            {
                throw new I2cException(path, Marshal.GetLastPInvokeError());
            }
            if (read != data.Length)
            {
                throw new I2cException(path, Errno.RemoteIo);
            }
        }
    }

    public void SmbusWriteBlock(byte command, ReadOnlySpan<byte> data)
    {
        if (data.Length > I2cTransfer.SmbusBlockMaximum)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }
        var block = stackalloc byte[SmbusDataLength];
        block[0] = (byte)data.Length;
        data.CopyTo(new Span<byte>(block + 1, data.Length));
        Smbus(SmbusWrite, command, block);
    }

    public void SmbusReadBlock(byte command, Span<byte> data)
    {
        if (data.Length > I2cTransfer.SmbusBlockMaximum)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }
        var block = stackalloc byte[SmbusDataLength];
        block[0] = (byte)data.Length;
        Smbus(SmbusRead, command, block);
        if (block[0] != data.Length)
        {
            throw new I2cException(path, Errno.RemoteIo);
        }
        new ReadOnlySpan<byte>(block + 1, data.Length).CopyTo(data);
    }

    public void Dispose()
    {
        if (descriptor >= 0)
        {
            LibC.Close(descriptor);
            descriptor = -1;
        }
    }

    private void Smbus(byte readWrite, byte command, byte* block)
    {
        var request = new I2cSmbusIoctlData { ReadWrite = readWrite, Command = command, Size = SmbusI2cBlockData, Data = block };
        Check(LibC.IoctlPointer(descriptor, SmbusTransfer, &request));
    }

    private void Check(int result)
    {
        if (result < 0)
        {
            throw new I2cException(path, Marshal.GetLastPInvokeError());
        }
    }
}
