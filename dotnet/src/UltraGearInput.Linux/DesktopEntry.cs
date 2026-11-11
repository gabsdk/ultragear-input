using System.Text;
using UltraGearInput.Core;

namespace UltraGearInput.Linux;

internal static class DesktopEntry
{
    public const string Icon = "video-display";

    public static string Render(string executable) => $"""
        [Desktop Entry]
        Type=Application
        Name={AppText.AppName}
        Comment=Switch the monitor input from the top bar
        Exec={QuoteExec(executable)}
        Icon={Icon}
        Terminal=false
        Categories=Utility;Settings;HardwareSettings;
        Keywords=monitor;display;input;ddc;lg;ultragear;
        StartupNotify=false
        X-GNOME-Autostart-enabled=true

        """;

    public static string QuoteExec(string executable)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in executable)
        {
            if (character is '"' or '`' or '$' or '\\')
            {
                builder.Append('\\');
            }
            builder.Append(character);
        }
        return builder.Append('"').ToString().Replace("%", "%%", StringComparison.Ordinal);
    }

    public static bool IsInstalled(string file) => File.Exists(file);

    public static void Write(string file, string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, Render(executable));
        File.Move(temporary, file, overwrite: true);
    }

    public static void Remove(string file)
    {
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }
}
