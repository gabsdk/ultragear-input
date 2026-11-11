using System.Runtime.Versioning;
using UltraGearInput.Core;

namespace UltraGearInput.Linux.Hardware;

internal enum SwitchFailure
{
    DisplayNotFound,
    NoChannel,
    DeviceMissing,
    PermissionDenied,
    Unsupported,
    Rejected,
}

internal sealed class SwitchException(SwitchFailure reason, string devicePath = "", int errno = 0) : Exception(reason.ToString())
{
    public SwitchFailure Reason { get; } = reason;

    public string DevicePath { get; } = devicePath;

    public int Errno { get; } = errno;

    public static SwitchException FromErrno(I2cException exception) => exception.Errno switch
    {
        Hardware.Errno.NoEntry or Hardware.Errno.NoDevice => new(SwitchFailure.DeviceMissing, exception.DevicePath, exception.Errno),
        Hardware.Errno.AccessDenied or Hardware.Errno.NoPermission => new(SwitchFailure.PermissionDenied, exception.DevicePath, exception.Errno),
        Hardware.Errno.NotTty => new(SwitchFailure.Unsupported, exception.DevicePath, exception.Errno),
        _ => new(SwitchFailure.Rejected, exception.DevicePath, exception.Errno),
    };
}

[SupportedOSPlatform("linux")]
internal static class LinuxDdc
{
    public static void WriteFrame(string devicePath, ReadOnlySpan<byte> frame)
    {
        try
        {
            using var device = I2cDevice.Open(devicePath);
            device.SelectAddress(I2cTransfer.DdcAddress);
            switch (I2cTransfer.WriteMethod(device.Functionality()))
            {
                case I2cWriteMethod.PlainWrite:
                    device.Write(frame);
                    break;
                case I2cWriteMethod.SmbusI2cBlock:
                    device.SmbusWriteBlock(frame[0], frame[1..]);
                    break;
                default:
                    throw new SwitchException(SwitchFailure.Unsupported, devicePath);
            }
        }
        catch (I2cException exception)
        {
            throw SwitchException.FromErrno(exception);
        }
    }

    public static byte[]? TryReadEdid(string devicePath)
    {
        try
        {
            using var device = I2cDevice.Open(devicePath);
            device.SelectAddress(I2cTransfer.EdidAddress);
            var edid = new byte[Edid.BlockLength];
            var functionality = device.Functionality();
            if (functionality.HasFlag(I2cFunctionality.PlainI2c))
            {
                device.Write([0x00]);
                device.Read(edid);
            }
            else if (functionality.HasFlag(I2cFunctionality.SmbusReadI2cBlock))
            {
                for (var offset = 0; offset < edid.Length; offset += I2cTransfer.SmbusBlockMaximum)
                {
                    device.SmbusReadBlock((byte)offset, edid.AsSpan(offset, I2cTransfer.SmbusBlockMaximum));
                }
            }
            else
            {
                return null;
            }
            return Edid.IsValid(edid) ? edid : null;
        }
        catch (I2cException)
        {
            return null;
        }
    }
}
