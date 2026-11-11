using System.Globalization;
using UltraGearInput.Core;

namespace UltraGearInput.Windows;

internal static class Text
{
    public const string StartWithWindows = "Start with Windows";
    public const string Exit = "Exit";
    public const string StartupFailed = "Could not change Start with Windows";
    public const string AlreadyRunning = "UltraGear Input is already running in the notification area.";

    public static TrayCommandLabels Labels { get; } = new(StartWithWindows, Exit);

    public static string Describe(SwitchException error) => error.Reason switch
    {
        SwitchFailure.DisplayNotFound => AppText.DisplayNotConnected,
        SwitchFailure.LgProtocolUnavailable => "The LG protocol needs an NVIDIA or AMD graphics driver. Try the Standard DDC/CI protocol.",
        _ => AppText.CommandRejected(string.Format(CultureInfo.InvariantCulture, "code {0}", error.Code)),
    };
}
