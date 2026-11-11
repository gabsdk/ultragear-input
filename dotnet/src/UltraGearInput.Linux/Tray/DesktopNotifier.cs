using UltraGearInput.Core;
using UltraGearInput.Linux.DBus;

namespace UltraGearInput.Linux.Tray;

internal sealed class DesktopNotifier(IDBusSender bus, TextWriter log)
{
    public const string Name = "org.freedesktop.Notifications";
    public const string Path = "/org/freedesktop/Notifications";
    public const int Timeout = 8000;

    public async Task ShowAsync(string summary, string body)
    {
        await log.WriteLineAsync($"{summary}: {body}").ConfigureAwait(false);
        var call = DBusMessage.MethodCall(
            Name,
            Path,
            Name,
            "Notify",
            "susssasa{sv}i",
            AppText.AppName,
            0u,
            DesktopEntry.Icon,
            summary,
            body,
            new List<object>(),
            new List<object>(),
            Timeout) with
        {
            Flags = DBusMessageFlags.NoReplyExpected,
        };
        await bus.SendAsync(call).ConfigureAwait(false);
    }
}
