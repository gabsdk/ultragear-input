using UltraGearInput.Core;

namespace UltraGearInput.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("ugi-").FullName;

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string CreateDirectory(params string[] parts) => Directory.CreateDirectory(Combine(parts)).FullName;

    public void WriteText(string text, params string[] parts)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Combine(parts))!);
        File.WriteAllText(Combine(parts), text);
    }

    public void WriteBytes(byte[] bytes, params string[] parts)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Combine(parts))!);
        File.WriteAllBytes(Combine(parts), bytes);
    }

    public void Link(string target, params string[] parts)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Combine(parts))!);
        File.CreateSymbolicLink(Combine(parts), target);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal static class Displays
{
    public static readonly DisplayDescriptor Lg = new("edid-lg", "LG ULTRAGEAR", "GSM");

    public static readonly DisplayDescriptor Dell = new("edid-dell", "DELL U2723QE", "DEL");

    public static IReadOnlyList<TrayMenuEntry> Menu(params DisplayDescriptor[] displays) =>
        TrayMenu.Build(displays, new SettingsLoad(new Settings(), null), new TrayCommandLabels("Start at Login", "Quit"), startsAtLogin: false);
}
