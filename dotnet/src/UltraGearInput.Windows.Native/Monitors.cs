using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltraGearInput.Windows.Native;

public sealed record NativeMonitor(nint Handle, string GdiDeviceName, int PhysicalIndex, string FriendlyName, string DevicePath);

[SupportedOSPlatform("windows")]
public static unsafe class Monitors
{
    private sealed record Target(string GdiDeviceName, string FriendlyName, string DevicePath);

    public static IReadOnlyList<NativeMonitor> Enumerate()
    {
        var targets = ActiveTargets();
        var monitors = new List<NativeMonitor>();
        foreach (var handle in MonitorHandles())
        {
            var gdiDeviceName = GdiDeviceName(handle);
            if (gdiDeviceName is null || !Win32.GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out var count))
            {
                continue;
            }
            var matching = targets.Where(target => DeviceNames.Same(target.GdiDeviceName, gdiDeviceName)).ToList();
            for (var index = 0; index < count; index++)
            {
                var target = index < matching.Count ? matching[index] : null;
                monitors.Add(new NativeMonitor(handle, gdiDeviceName, index, target?.FriendlyName ?? "", target?.DevicePath ?? ""));
            }
        }
        return monitors;
    }

    public static bool TrySetVcpFeature(NativeMonitor monitor, byte code, uint value, out int error)
    {
        error = 0;
        if (!Win32.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Handle, out var count) || monitor.PhysicalIndex >= count)
        {
            error = Marshal.GetLastPInvokeError();
            return false;
        }
        var physical = new PhysicalMonitor[count];
        fixed (PhysicalMonitor* pointer = physical)
        {
            if (!Win32.GetPhysicalMonitorsFromHMONITOR(monitor.Handle, count, pointer))
            {
                error = Marshal.GetLastPInvokeError();
                return false;
            }
            try
            {
                if (Win32.SetVCPFeature(physical[monitor.PhysicalIndex].Handle, code, value))
                {
                    return true;
                }
                error = Marshal.GetLastPInvokeError();
                return false;
            }
            finally
            {
                Win32.DestroyPhysicalMonitors(count, pointer);
            }
        }
    }

    private static List<Target> ActiveTargets()
    {
        DisplayConfigPathInfo[] paths;
        uint pathCount;
        while (true)
        {
            if (Win32.GetDisplayConfigBufferSizes(Win32.QueryOnlyActivePaths, out pathCount, out var modeCount) != Win32.Success)
            {
                return [];
            }
            paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            int result;
            fixed (DisplayConfigPathInfo* pathPointer = paths)
            fixed (DisplayConfigModeInfo* modePointer = modes)
            {
                result = Win32.QueryDisplayConfig(Win32.QueryOnlyActivePaths, ref pathCount, pathPointer, ref modeCount, modePointer, 0);
            }
            if (result == Win32.Success)
            {
                break;
            }
            if (result != Win32.InsufficientBuffer)
            {
                return [];
            }
        }
        return [.. paths.Take((int)pathCount).Select(TargetFor).OfType<Target>()];
    }

    private static Target? TargetFor(DisplayConfigPathInfo path)
    {
        var source = new DisplayConfigSourceDeviceName
        {
            Header = Header(Win32.DeviceInfoGetSourceName, (uint)sizeof(DisplayConfigSourceDeviceName), path.SourceInfo.AdapterId, path.SourceInfo.Id),
        };
        var target = new DisplayConfigTargetDeviceName
        {
            Header = Header(Win32.DeviceInfoGetTargetName, (uint)sizeof(DisplayConfigTargetDeviceName), path.TargetInfo.AdapterId, path.TargetInfo.Id),
        };
        if (Win32.DisplayConfigGetDeviceInfo(&source.Header) != Win32.Success
            || Win32.DisplayConfigGetDeviceInfo(&target.Header) != Win32.Success)
        {
            return null;
        }
        return new Target(
            NativeText.FromUtf16(source.ViewGdiDeviceName, 32),
            NativeText.FromUtf16(target.MonitorFriendlyDeviceName, 64),
            NativeText.FromUtf16(target.MonitorDevicePath, 128));
    }

    private static DisplayConfigDeviceInfoHeader Header(uint type, uint size, Luid adapterId, uint id) =>
        new() { Type = type, Size = size, AdapterId = adapterId, Id = id };

    private static List<nint> MonitorHandles()
    {
        var handles = new List<nint>();
        var handle = GCHandle.Alloc(handles);
        try
        {
            Win32.EnumDisplayMonitors(0, 0, &CollectMonitor, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return handles;
    }

    [UnmanagedCallersOnly]
    private static int CollectMonitor(nint monitor, nint deviceContext, nint rect, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> handles)
        {
            handles.Add(monitor);
        }
        return 1;
    }

    private static string? GdiDeviceName(nint monitor)
    {
        var info = new MonitorInfoEx { Size = (uint)sizeof(MonitorInfoEx) };
        return Win32.GetMonitorInfo(monitor, &info) ? NativeText.FromUtf16(info.Device, 32) : null;
    }
}
