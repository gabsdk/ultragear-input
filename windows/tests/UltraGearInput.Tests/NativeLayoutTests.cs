using UltraGearInput.Native;

namespace UltraGearInput.Tests;

public sealed unsafe class NativeLayoutTests
{
    [Fact]
    public void Win32StructuresMatchSdkSizes()
    {
        Assert.Equal(104, sizeof(MonitorInfoEx));
        Assert.Equal(264, sizeof(PhysicalMonitor));
        Assert.Equal(72, sizeof(DisplayConfigPathInfo));
        Assert.Equal(64, sizeof(DisplayConfigModeInfo));
        Assert.Equal(20, sizeof(DisplayConfigDeviceInfoHeader));
        Assert.Equal(84, sizeof(DisplayConfigSourceDeviceName));
        Assert.Equal(420, sizeof(DisplayConfigTargetDeviceName));
    }

    [Fact]
    public void Win32FieldOffsetsMatchSdk()
    {
        var monitor = default(MonitorInfoEx);
        Assert.Equal(40, Offset(&monitor, monitor.Device));

        var physical = default(PhysicalMonitor);
        Assert.Equal(8, Offset(&physical, physical.Description));

        var path = default(DisplayConfigPathInfo);
        Assert.Equal(20, Offset(&path, &path.TargetInfo));
        Assert.Equal(68, Offset(&path, &path.Flags));

        var target = default(DisplayConfigTargetDeviceName);
        Assert.Equal(28, Offset(&target, &target.EdidManufactureId));
        Assert.Equal(36, Offset(&target, target.MonitorFriendlyDeviceName));
        Assert.Equal(164, Offset(&target, target.MonitorDevicePath));
    }

    [Fact]
    public void NvidiaI2cInfoMatchesVersionThreeLayout()
    {
        var info = default(NvI2cInfo);
        Assert.Equal(64, sizeof(NvI2cInfo));
        Assert.Equal(9, Offset(&info, &info.DeviceAddress));
        Assert.Equal(16, Offset(&info, &info.RegisterAddress));
        Assert.Equal(32, Offset(&info, &info.Data));
        Assert.Equal(52, Offset(&info, &info.PortId));
        Assert.Equal(56, Offset(&info, &info.IsPortIdSet));
        Assert.Equal(0x0003_0040u, NvI2cInfo.CurrentVersion);
    }

    [Fact]
    public void NvidiaInterfaceIdsMatchSdk()
    {
        Assert.Equal(0x0150E828u, NvApiInterface.Initialize);
        Assert.Equal(0x9ABDD40Du, NvApiInterface.EnumNvidiaDisplayHandle);
        Assert.Equal(0x22A78B05u, NvApiInterface.GetAssociatedNvidiaDisplayName);
        Assert.Equal(0xD995937Eu, NvApiInterface.GetAssociatedDisplayOutputId);
        Assert.Equal(0x34EF9506u, NvApiInterface.GetPhysicalGpusFromDisplay);
        Assert.Equal(0xE812EB07u, NvApiInterface.I2cWrite);
    }

    [Fact]
    public void AmdStructuresMatchSdkLayout()
    {
        var adapter = default(AdlAdapterInfo);
        Assert.Equal(1572, sizeof(AdlAdapterInfo));
        Assert.Equal(536, Offset(&adapter, adapter.DisplayName));
        Assert.Equal(1568, Offset(&adapter, &adapter.OsDisplayIndex));

        var display = default(AdlDisplayInfo);
        Assert.Equal(552, sizeof(AdlDisplayInfo));
        Assert.Equal(20, Offset(&display, display.DisplayName));
        Assert.Equal(548, Offset(&display, &display.InfoValue));
    }

    private static long Offset(void* structure, void* field) => (byte*)field - (byte*)structure;
}
