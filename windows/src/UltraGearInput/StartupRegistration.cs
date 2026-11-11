using System.Runtime.Versioning;
using Microsoft.Win32;

namespace UltraGearInput;

[SupportedOSPlatform("windows")]
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return string.Equals(key?.GetValue(Text.AppName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(Text.AppName, Command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(Text.AppName, throwOnMissingValue: false);
        }
    }
}
