using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltraGearInput.Windows.Native;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdlAdapterInfo
{
    public int Size;
    public int AdapterIndex;
    public fixed byte Udid[Adl.MaximumPath];
    public int BusNumber;
    public int DeviceNumber;
    public int FunctionNumber;
    public int VendorId;
    public fixed byte AdapterName[Adl.MaximumPath];
    public fixed byte DisplayName[Adl.MaximumPath];
    public int Present;
    public int Exist;
    public fixed byte DriverPath[Adl.MaximumPath];
    public fixed byte DriverPathExt[Adl.MaximumPath];
    public fixed byte PnpString[Adl.MaximumPath];
    public int OsDisplayIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdlDisplayId
{
    public int LogicalIndex;
    public int PhysicalIndex;
    public int LogicalAdapterIndex;
    public int PhysicalAdapterIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AdlDisplayInfo
{
    public AdlDisplayId DisplayId;
    public int ControllerIndex;
    public fixed byte DisplayName[Adl.MaximumPath];
    public fixed byte ManufacturerName[Adl.MaximumPath];
    public int DisplayType;
    public int OutputType;
    public int Connector;
    public int InfoMask;
    public int InfoValue;
}

public sealed record AmdDisplay(string GdiDeviceName, int AdapterIndex, int DisplayIndex);

[SupportedOSPlatform("windows")]
public sealed unsafe class Adl : IDisposable
{
    public const int Ok = 0;
    internal const int MaximumPath = 256;
    internal const int ConnectedAndMapped = 0x3;

    private readonly delegate* unmanaged<int> destroy;
    private readonly delegate* unmanaged<int*, int> numberOfAdapters;
    private readonly delegate* unmanaged<AdlAdapterInfo*, int, int> adapterInfo;
    private readonly delegate* unmanaged<int, int*, AdlDisplayInfo**, int, int> displayInfo;
    private readonly delegate* unmanaged<int, int, int, int, int, byte*, int*, byte*, int> ddcBlockAccess;
    private bool disposed;

    private Adl(nint library)
    {
        destroy = (delegate* unmanaged<int>)NativeText.Export(library, "ADL_Main_Control_Destroy");
        numberOfAdapters = (delegate* unmanaged<int*, int>)NativeText.Export(library, "ADL_Adapter_NumberOfAdapters_Get");
        adapterInfo = (delegate* unmanaged<AdlAdapterInfo*, int, int>)NativeText.Export(library, "ADL_Adapter_AdapterInfo_Get");
        displayInfo = (delegate* unmanaged<int, int*, AdlDisplayInfo**, int, int>)NativeText.Export(library, "ADL_Display_DisplayInfo_Get");
        ddcBlockAccess = (delegate* unmanaged<int, int, int, int, int, byte*, int*, byte*, int>)NativeText.Export(library, "ADL_Display_DDCBlockAccess_Get");
    }

    private bool IsComplete =>
        destroy != null && numberOfAdapters != null && adapterInfo != null && displayInfo != null && ddcBlockAccess != null;

    public static Adl? TryLoad()
    {
        if (!NativeLibrary.TryLoad("atiadlxx.dll", out var library))
        {
            return null;
        }
        var create = (delegate* unmanaged<delegate* unmanaged<int, nint>, int, int>)NativeText.Export(library, "ADL_Main_Control_Create");
        var api = new Adl(library);
        if (create == null || !api.IsComplete || create(&Allocate, 1) != Ok)
        {
            return null;
        }
        return api;
    }

    public IReadOnlyList<AmdDisplay> Displays()
    {
        int count;
        if (numberOfAdapters(&count) != Ok || count <= 0)
        {
            return [];
        }
        var adapters = new AdlAdapterInfo[count];
        fixed (AdlAdapterInfo* pointer = adapters)
        {
            if (adapterInfo(pointer, sizeof(AdlAdapterInfo) * count) != Ok)
            {
                return [];
            }
        }
        var displays = new List<AmdDisplay>();
        foreach (var adapter in adapters)
        {
            var gdiDeviceName = NativeText.FromAnsi(adapter.DisplayName, MaximumPath);
            if (gdiDeviceName.Length == 0 || displays.Any(display => DeviceNames.Same(display.GdiDeviceName, gdiDeviceName)))
            {
                continue;
            }
            if (MappedDisplayIndex(adapter.AdapterIndex) is { } displayIndex)
            {
                displays.Add(new AmdDisplay(gdiDeviceName, adapter.AdapterIndex, displayIndex));
            }
        }
        return displays;
    }

    public int Write(AmdDisplay display, ReadOnlySpan<byte> frameWithDestination)
    {
        var buffer = frameWithDestination.ToArray();
        var received = 0;
        fixed (byte* pointer = buffer)
        {
            return ddcBlockAccess(display.AdapterIndex, display.DisplayIndex, 0, 0, buffer.Length, pointer, &received, null);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        destroy();
    }

    private int? MappedDisplayIndex(int adapterIndex)
    {
        int count;
        AdlDisplayInfo* infos = null;
        if (displayInfo(adapterIndex, &count, &infos, 0) != Ok || infos == null)
        {
            return null;
        }
        try
        {
            for (var index = 0; index < count; index++)
            {
                var info = infos[index];
                if ((info.InfoValue & ConnectedAndMapped) == ConnectedAndMapped && info.DisplayId.LogicalAdapterIndex == adapterIndex)
                {
                    return info.DisplayId.LogicalIndex;
                }
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal((nint)infos);
        }
    }

    [UnmanagedCallersOnly]
    private static nint Allocate(int size) => Marshal.AllocHGlobal(size);
}
