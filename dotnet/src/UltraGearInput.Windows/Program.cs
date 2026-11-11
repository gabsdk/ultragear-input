using System.Runtime.Versioning;
using UltraGearInput.Core;
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
        if (args.Contains(ListDisplaysFlag, StringComparer.Ordinal))
        {
            return ListDisplays();
        }
        using var instance = new Mutex(initiallyOwned: true, InstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(Text.AlreadyRunning, AppText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        ApplicationConfiguration.Initialize();
        using var application = new TrayApplication(Store);
        Application.Run(application);
        return 0;
    }

    private static int ListDisplays()
    {
        if (!Console.IsOutputRedirected)
        {
            ParentConsole.Attach();
        }
        var settings = Store.Load().Settings;
        var reports = InputSwitcher.Displays().Select(display => DisplayReport.From(display.Descriptor, settings)).ToList();
        using var output = new StreamWriter(Console.OpenStandardOutput());
        output.WriteLine(DisplayReport.Encode(reports));
        return 0;
    }
}
