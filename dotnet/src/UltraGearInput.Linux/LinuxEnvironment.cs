namespace UltraGearInput.Linux;

internal sealed record LinuxEnvironment(string SysfsRoot, string DeviceRoot, string ConfigHome, string DataHome, string Executable)
{
    public const string ApplicationId = "ultragear-input";
    public const string SysfsVariable = "ULTRAGEAR_INPUT_SYSFS";
    public const string DeviceVariable = "ULTRAGEAR_INPUT_DEV";

    public string SettingsFile => Path.Combine(ConfigHome, ApplicationId, "settings.json");

    public string AutostartFile => Path.Combine(ConfigHome, "autostart", ApplicationId + ".desktop");

    public string LauncherFile => Path.Combine(DataHome, "applications", ApplicationId + ".desktop");

    public static LinuxEnvironment From(Func<string, string?> variable, string home, string executable) => new(
        NonEmpty(variable(SysfsVariable)) ?? "/sys",
        NonEmpty(variable(DeviceVariable)) ?? "/dev",
        NonEmpty(variable("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config"),
        NonEmpty(variable("XDG_DATA_HOME")) ?? Path.Combine(home, ".local", "share"),
        executable);

    public static LinuxEnvironment Current() => From(
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.ProcessPath ?? ApplicationId);

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
