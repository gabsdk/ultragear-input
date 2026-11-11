using System.Runtime.Versioning;
using UltraGearInput.Core;
using UltraGearInput.Windows.Native;

namespace UltraGearInput.Windows;

internal enum SwitchFailure
{
    DisplayNotFound,
    LgProtocolUnavailable,
    CommandRejected,
}

internal sealed class SwitchException(SwitchFailure reason, int code = 0) : Exception(reason.ToString())
{
    public SwitchFailure Reason { get; } = reason;

    public int Code { get; } = code;
}

internal sealed record WindowsDisplay(DisplayDescriptor Descriptor, NativeMonitor Monitor);

[SupportedOSPlatform("windows")]
internal static class InputSwitcher
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static IReadOnlyList<WindowsDisplay> Displays() =>
        [.. Monitors.Enumerate().Select(monitor => new WindowsDisplay(Describe(monitor), monitor))];

    public static async Task SwitchAsync(string displayKey, ushort value, DdcProtocol ddcProtocol)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() => Switch(displayKey, value, ddcProtocol)).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void Switch(string displayKey, ushort value, DdcProtocol ddcProtocol)
    {
        var display = Displays().FirstOrDefault(candidate => candidate.Descriptor.Key == displayKey)
            ?? throw new SwitchException(SwitchFailure.DisplayNotFound);
        if (ddcProtocol == DdcProtocol.Standard)
        {
            if (!Monitors.TrySetVcpFeature(display.Monitor, ddcProtocol.InputCode, value, out var error))
            {
                throw new SwitchException(SwitchFailure.CommandRejected, error);
            }
            return;
        }
        WriteWithVendorDriver(display.Monitor.GdiDeviceName, DdcFrame.SetInput(value, ddcProtocol));
    }

    private static void WriteWithVendorDriver(string gdiDeviceName, byte[] frame)
    {
        if (NvApi.TryLoad() is { } nvidia
            && nvidia.Displays().FirstOrDefault(display => DeviceNames.Same(display.GdiDeviceName, gdiDeviceName)) is { } nvidiaDisplay)
        {
            Ensure(nvidia.Write(nvidiaDisplay, frame));
            return;
        }
        using var amd = Adl.TryLoad();
        if (amd?.Displays().FirstOrDefault(display => DeviceNames.Same(display.GdiDeviceName, gdiDeviceName)) is { } amdDisplay)
        {
            Ensure(amd.Write(amdDisplay, DdcFrame.WithDestination(frame)));
            return;
        }
        throw new SwitchException(SwitchFailure.LgProtocolUnavailable);
    }

    private static void Ensure(int status)
    {
        if (status != 0)
        {
            throw new SwitchException(SwitchFailure.CommandRejected, status);
        }
    }

    private static DisplayDescriptor Describe(NativeMonitor monitor)
    {
        var key = monitor.DevicePath.Length > 0
            ? monitor.DevicePath
            : $"{monitor.GdiDeviceName}#{monitor.PhysicalIndex}";
        var name = monitor.FriendlyName.Length > 0 ? monitor.FriendlyName : AppText.UnknownDisplay;
        return new DisplayDescriptor(key, name, ManufacturerCode.FromDevicePath(monitor.DevicePath));
    }
}
