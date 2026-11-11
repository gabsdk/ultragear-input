using System.Runtime.Versioning;
using UltraGearInput.Core;
using UltraGearInput.Native;

namespace UltraGearInput;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string ListDisplaysFlag = "--list-displays";
    private const string InstanceName = @"Local\UltraGearInput";

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
            MessageBox.Show(Text.AlreadyRunning, Text.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        ApplicationConfiguration.Initialize();
        using var application = new TrayApplication(SettingsStore.Standard);
        Application.Run(application);
        return 0;
    }

    private static int ListDisplays()
    {
        if (!Console.IsOutputRedirected)
        {
            ParentConsole.Attach();
        }
        var settings = SettingsStore.Standard.Load().Settings;
        var reports = InputSwitcher.Displays().Select(display => DisplayReport.From(display.Descriptor, settings)).ToList();
        using var output = new StreamWriter(Console.OpenStandardOutput());
        output.WriteLine(DisplayReport.Encode(reports));
        return 0;
    }
}
