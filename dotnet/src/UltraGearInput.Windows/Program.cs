using System.Runtime.Versioning;
using UltraGearInput.Core;
using UltraGearInput.Core.Updates;
using UltraGearInput.Windows.Native;

namespace UltraGearInput.Windows;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string ListDisplaysFlag = "--list-displays";
    private const string InstanceName = @"Local\UltraGearInput";

    private static SettingsStore Store { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppText.AppName,
        "settings.json"));

    [STAThread]
    private static int Main(string[] args)
    {
        UpdateText.PreferredLanguage = UpdateText.WindowsLanguage(SystemLocale.UserLanguageId);
        if (args is [UpdateInstaller.ApplyFlag, var plan])
        {
            return UpdateInstaller.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
        }
        if (args is ["--version"])
        {
            return WriteOutput(UpdateInstaller.CurrentVersion);
        }
        if (args.Contains(ListDisplaysFlag, StringComparer.Ordinal))
        {
            return ListDisplays();
        }
        if (args is [UpdateInstaller.UpdatedFlag or UpdateInstaller.FailedFlag, _, var stage, var helper]
            && int.TryParse(helper, System.Globalization.CultureInfo.InvariantCulture, out var helperId))
        {
            _ = UpdateInstaller.CleanupAsync(stage, helperId);
            args = args[..2];
        }
        using var instance = new Mutex(initiallyOwned: true, InstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(Text.AlreadyRunning, AppText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        ApplicationConfiguration.Initialize();
        using var application = new TrayApplication(Store);
        if (args is [UpdateInstaller.UpdatedFlag, var version])
        {
            MessageBox.Show(UpdateText.Get("updated", value: ReleaseVersion.Parse(version).ToString()), AppText.AppName);
        }
        else if (args is [UpdateInstaller.FailedFlag, _])
        {
            MessageBox.Show(UpdateText.Get("installFailed"), AppText.AppName);
        }
        Application.Run(application);
        return 0;
    }

    private static int ListDisplays()
    {
        var settings = Store.Load().Settings;
        var reports = InputSwitcher.Displays().Select(display => DisplayReport.From(display.Descriptor, settings)).ToList();
        return WriteOutput(DisplayReport.Encode(reports));
    }

    private static int WriteOutput(string text)
    {
        if (!Console.IsOutputRedirected)
        {
            ParentConsole.Attach();
        }
        using var output = new StreamWriter(Console.OpenStandardOutput());
        output.WriteLine(text);
        return 0;
    }
}
