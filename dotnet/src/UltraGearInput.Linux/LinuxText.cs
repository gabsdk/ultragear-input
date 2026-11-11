using System.Globalization;
using UltraGearInput.Core;
using UltraGearInput.Linux.Hardware;

namespace UltraGearInput.Linux;

internal static class LinuxText
{
    public const string StartAtLogin = "Start at Login";
    public const string Quit = "Quit UltraGear Input";
    public const string StartupFailed = "Could not change Start at Login";
    public const string OpenSettingsFailed = "Could not open the settings file";
    public const string AlreadyRunning = "UltraGear Input is already running.";
    public const string NoSessionBus = "No D-Bus session bus found. Start UltraGear Input from your desktop session.";
    public const string NoTrayHostTitle = "UltraGear Input is running without a top bar icon";
    public const string NoTrayHostBody = "Your desktop has no tray host. On GNOME, install and enable the AppIndicator extension, then log out and back in.";

    public static TrayCommandLabels Labels { get; } = new(StartAtLogin, Quit);

    public static string Describe(SwitchException error) => error.Reason switch
    {
        SwitchFailure.DisplayNotFound => AppText.DisplayNotConnected,
        SwitchFailure.NoChannel => "No DDC/CI channel was found for this display. Connect it straight to the graphics card, without a dock or adapter.",
        SwitchFailure.DeviceMissing => string.Format(CultureInfo.InvariantCulture, "{0} does not exist. Load the I2C driver with: sudo modprobe i2c-dev", error.DevicePath),
        SwitchFailure.PermissionDenied => string.Format(CultureInfo.InvariantCulture, "No permission to use {0}. Run install.sh again, then log out and back in.", error.DevicePath),
        SwitchFailure.Unsupported => string.Format(CultureInfo.InvariantCulture, "The graphics driver does not allow DDC/CI writes on {0}.", error.DevicePath),
        _ => AppText.CommandRejected(string.Format(CultureInfo.InvariantCulture, "errno {0}", error.Errno)),
    };
}
