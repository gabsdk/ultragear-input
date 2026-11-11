using System.Globalization;
using UltraGearInput.Core;

namespace UltraGearInput;

internal static class Text
{
    public const string AppName = "UltraGear Input";
    public const string UnknownDisplay = "External Display";
    public const string NoDisplays = "No external display found";
    public const string ProtocolMenu = "Protocol";
    public const string OpenSettings = "Open Settings File…";
    public const string StartWithWindows = "Start with Windows";
    public const string Exit = "Exit";
    public const string SwitchFailed = "Could not switch the input";
    public const string SettingsFailed = "Could not save the settings";
    public const string StartupFailed = "Could not change Start with Windows";
    public const string AlreadyRunning = "UltraGear Input is already running in the notification area.";

    public static string ProtocolName(DdcProtocol ddcProtocol) => ddcProtocol switch
    {
        DdcProtocol.Lg => "LG",
        _ => "Standard DDC/CI",
    };

    public static string InvalidSettings(SettingsError error) => error switch
    {
        SettingsError.EmptyInputs => "Settings file has an empty input list. Using defaults.",
        SettingsError.TooManyInputs => string.Format(CultureInfo.InvariantCulture, "Settings file has more than {0} inputs. Using defaults.", Settings.MaximumInputs),
        SettingsError.BlankInputId => "Settings file has an input without an id. Using defaults.",
        SettingsError.DuplicateInputId => "Settings file repeats an input id. Using defaults.",
        SettingsError.BlankInputName => "Settings file has an input without a name. Using defaults.",
        _ => "Settings file is not valid JSON. Using defaults.",
    };

    public static string Describe(SwitchException error) => error.Reason switch
    {
        SwitchFailure.DisplayNotFound => "The display is no longer connected.",
        SwitchFailure.LgProtocolUnavailable => "The LG protocol needs an NVIDIA or AMD graphics driver. Try the Standard DDC/CI protocol.",
        _ => string.Format(CultureInfo.InvariantCulture, "The display did not accept the command (code {0}). Try the other protocol, another cable or another port.", error.Code),
    };
}
