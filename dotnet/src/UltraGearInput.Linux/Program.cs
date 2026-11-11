using System.Reflection;
using System.Runtime.Versioning;
using UltraGearInput.Core;
using UltraGearInput.Core.Updates;
using UltraGearInput.Linux;
using UltraGearInput.Linux.DBus;
using UltraGearInput.Linux.Hardware;

if (!OperatingSystem.IsLinux())
{
    await Console.Error.WriteLineAsync("ultragear-input runs on Linux only.").ConfigureAwait(false);
    return 1;
}
return await LinuxProgram.RunAsync(args).ConfigureAwait(false);

[SupportedOSPlatform("linux")]
internal static class LinuxProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is [UpdateInstaller.ApplyFlag, var plan])
        {
            return await UpdateInstaller.ApplyAsync(plan, CancellationToken.None).ConfigureAwait(false);
        }
        if (args is [UpdateInstaller.UpdatedFlag or UpdateInstaller.FailedFlag, _, var stage, var helper]
            && int.TryParse(helper, System.Globalization.CultureInfo.InvariantCulture, out var helperId))
        {
            _ = UpdateInstaller.CleanupAsync(stage, helperId);
            args = args[..2];
        }
        var environment = LinuxEnvironment.Current();
        string? notice = null;
        var store = new SettingsStore(environment.SettingsFile);
        var inventory = new DisplayInventory(
            new SysfsScanner(environment.SysfsRoot),
            bus => LinuxDdc.TryReadEdid(I2cTransfer.DevicePath(environment.DeviceRoot, bus)));

        switch (args)
        {
            case ["--list-displays"]:
                var settings = store.Load().Settings;
                Console.WriteLine(DisplayReport.Encode([.. inventory.Scan().Select(display => DisplayReport.From(display.Descriptor, settings))]));
                return 0;
            case ["--install-launcher"]:
                DesktopEntry.Write(environment.LauncherFile, environment.Executable);
                Console.WriteLine(environment.LauncherFile);
                return 0;
            case ["--version"]:
                Console.WriteLine(typeof(LinuxProgram).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]);
                return 0;
            case [UpdateInstaller.UpdatedFlag, var version]:
                notice = UpdateText.Get("updated", value: ReleaseVersion.Parse(version).ToString());
                break;
            case [UpdateInstaller.FailedFlag, _]:
                notice = UpdateText.Get("installFailed");
                break;
            case []:
                break;
            default:
                await Console.Error.WriteLineAsync("Usage: ultragear-input [--list-displays | --install-launcher | --version]").ConfigureAwait(false);
                return 2;
        }

        if (DBusAddress.Session(Environment.GetEnvironmentVariable) is not { } endpoint)
        {
            await Console.Error.WriteLineAsync(LinuxText.NoSessionBus).ConfigureAwait(false);
            return 1;
        }
        return await new TrayHost(environment, inventory, store).RunAsync(endpoint, notice).ConfigureAwait(false);
    }
}
