using System.Runtime.Versioning;

namespace UltraGearInput.Windows.Native;

[SupportedOSPlatform("windows")]
public static class Win32Icons
{
    public static void Destroy(nint icon) => Win32.DestroyIcon(icon);
}

[SupportedOSPlatform("windows")]
public static class ParentConsole
{
    public static bool Attach() => Win32.AttachConsole(Win32.AttachParentProcess);
}

[SupportedOSPlatform("windows")]
public static class SystemLocale
{
    public static ushort UserLanguageId => Win32.GetUserDefaultUILanguage();
}
