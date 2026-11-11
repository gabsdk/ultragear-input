using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltraGearInput.Windows.Native;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NvI2cInfo
{
    public uint Version;
    public uint DisplayMask;
    public byte IsDdcPort;
    public byte DeviceAddress;
    public byte* RegisterAddress;
    public uint RegisterAddressSize;
    public byte* Data;
    public uint DataSize;
    public uint Speed;
    public uint SpeedKhz;
    public byte PortId;
    public uint IsPortIdSet;

    public static uint CurrentVersion => (uint)sizeof(NvI2cInfo) | (3u << 16);
}

internal static class NvApiInterface
{
    public const uint Initialize = 0x0150E828;
    public const uint EnumNvidiaDisplayHandle = 0x9ABDD40D;
    public const uint GetAssociatedNvidiaDisplayName = 0x22A78B05;
    public const uint GetAssociatedDisplayOutputId = 0xD995937E;
    public const uint GetPhysicalGpusFromDisplay = 0x34EF9506;
    public const uint I2cWrite = 0xE812EB07;
}

public sealed record NvidiaDisplay(string GdiDeviceName, nint Gpu, uint OutputId);

[SupportedOSPlatform("windows")]
public sealed unsafe class NvApi
{
    public const int Ok = 0;

    private const int ShortStringLength = 64;
    private const int MaximumPhysicalGpus = 64;
    private const uint DeprecatedSpeed = 0xFFFF;

    private readonly delegate* unmanaged[Cdecl]<uint, nint*, int> enumNvidiaDisplayHandle;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, int> getAssociatedNvidiaDisplayName;
    private readonly delegate* unmanaged[Cdecl]<nint, uint*, int> getAssociatedDisplayOutputId;
    private readonly delegate* unmanaged[Cdecl]<nint, nint*, uint*, int> getPhysicalGpusFromDisplay;
    private readonly delegate* unmanaged[Cdecl]<nint, NvI2cInfo*, int> i2cWrite;

    private NvApi(delegate* unmanaged[Cdecl]<uint, nint> queryInterface)
    {
        enumNvidiaDisplayHandle = (delegate* unmanaged[Cdecl]<uint, nint*, int>)queryInterface(NvApiInterface.EnumNvidiaDisplayHandle);
        getAssociatedNvidiaDisplayName = (delegate* unmanaged[Cdecl]<nint, byte*, int>)queryInterface(NvApiInterface.GetAssociatedNvidiaDisplayName);
        getAssociatedDisplayOutputId = (delegate* unmanaged[Cdecl]<nint, uint*, int>)queryInterface(NvApiInterface.GetAssociatedDisplayOutputId);
        getPhysicalGpusFromDisplay = (delegate* unmanaged[Cdecl]<nint, nint*, uint*, int>)queryInterface(NvApiInterface.GetPhysicalGpusFromDisplay);
        i2cWrite = (delegate* unmanaged[Cdecl]<nint, NvI2cInfo*, int>)queryInterface(NvApiInterface.I2cWrite);
    }

    private bool IsComplete =>
        enumNvidiaDisplayHandle != null
        && getAssociatedNvidiaDisplayName != null
        && getAssociatedDisplayOutputId != null
        && getPhysicalGpusFromDisplay != null
        && i2cWrite != null;

    public static NvApi? TryLoad()
    {
        if (!NativeLibrary.TryLoad("nvapi64.dll", out var library))
        {
            return null;
        }
        var queryAddress = NativeText.Export(library, "nvapi_QueryInterface");
        if (queryAddress == 0)
        {
            return null;
        }
        var queryInterface = (delegate* unmanaged[Cdecl]<uint, nint>)queryAddress;
        var initialize = (delegate* unmanaged[Cdecl]<int>)queryInterface(NvApiInterface.Initialize);
        if (initialize == null || initialize() != Ok)
        {
            return null;
        }
        var api = new NvApi(queryInterface);
        return api.IsComplete ? api : null;
    }

    public IReadOnlyList<NvidiaDisplay> Displays()
    {
        var displays = new List<NvidiaDisplay>();
        nint handle;
        for (uint index = 0; enumNvidiaDisplayHandle(index, &handle) == Ok; index++)
        {
            if (Describe(handle) is { } display)
            {
                displays.Add(display);
            }
        }
        return displays;
    }

    public int Write(NvidiaDisplay display, ReadOnlySpan<byte> frame)
    {
        var registerAddress = frame[0];
        var payload = frame[1..].ToArray();
        fixed (byte* data = payload)
        {
            var info = new NvI2cInfo
            {
                Version = NvI2cInfo.CurrentVersion,
                DisplayMask = display.OutputId,
                IsDdcPort = 1,
                DeviceAddress = 0x6E,
                RegisterAddress = &registerAddress,
                RegisterAddressSize = 1,
                Data = data,
                DataSize = (uint)payload.Length,
                Speed = DeprecatedSpeed,
            };
            return i2cWrite(display.Gpu, &info);
        }
    }

    private NvidiaDisplay? Describe(nint handle)
    {
        var name = stackalloc byte[ShortStringLength];
        uint outputId;
        var gpus = stackalloc nint[MaximumPhysicalGpus];
        uint gpuCount;
        if (getAssociatedNvidiaDisplayName(handle, name) != Ok
            || getAssociatedDisplayOutputId(handle, &outputId) != Ok
            || getPhysicalGpusFromDisplay(handle, gpus, &gpuCount) != Ok
            || gpuCount == 0)
        {
            return null;
        }
        return new NvidiaDisplay(NativeText.FromAnsi(name, ShortStringLength), gpus[0], outputId);
    }
}
