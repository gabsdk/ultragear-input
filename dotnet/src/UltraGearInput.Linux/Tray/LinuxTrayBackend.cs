using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using UltraGearInput.Core;
using UltraGearInput.Linux.Hardware;

namespace UltraGearInput.Linux.Tray;

[SupportedOSPlatform("linux")]
internal sealed class LinuxTrayBackend(
    LinuxEnvironment environment,
    DisplayInventory inventory,
    SettingsStore store,
    DesktopNotifier notifier,
    Action quit) : ITrayBackend, IDisposable
{
    private readonly SemaphoreSlim switchGate = new(1, 1);
    private IReadOnlyList<LinuxDisplay> displays = [];

    public void Dispose() => switchGate.Dispose();

    public IReadOnlyList<TrayMenuEntry> BuildMenu()
    {
        var current = inventory.Scan();
        Volatile.Write(ref displays, current);
        return TrayMenu.Build(
            [.. current.Select(display => display.Descriptor)],
            store.Load(),
            LinuxText.Labels,
            DesktopEntry.IsInstalled(environment.AutostartFile));
    }

    public void Activate(TrayMenuEntry entry)
    {
        switch (entry)
        {
            case TrayInput input:
                _ = SwitchAsync(input);
                break;
            case TrayProtocolChoice choice:
                Report(AppText.SettingsFailed, () => store.SaveProtocol(choice.Protocol, choice.Display));
                break;
            case TrayCommand { Kind: TrayCommandKind.OpenSettings }:
                Report(LinuxText.OpenSettingsFailed, OpenSettings);
                break;
            case TrayCommand { Kind: TrayCommandKind.StartAtLogin }:
                Report(LinuxText.StartupFailed, ToggleAutostart);
                break;
            case TrayCommand { Kind: TrayCommandKind.Quit }:
                quit();
                break;
        }
    }

    private async Task SwitchAsync(TrayInput input)
    {
        await switchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() => Switch(input)).ConfigureAwait(false);
        }
        catch (SwitchException exception)
        {
            await notifier.ShowAsync(AppText.SwitchFailed, LinuxText.Describe(exception)).ConfigureAwait(false);
        }
        finally
        {
            switchGate.Release();
        }
    }

    private void Switch(TrayInput input)
    {
        var display = Volatile.Read(ref displays).FirstOrDefault(candidate => candidate.Descriptor.Key == input.Display.Key)
            ?? throw new SwitchException(SwitchFailure.DisplayNotFound);
        if (display.Bus is not { } bus)
        {
            throw new SwitchException(SwitchFailure.NoChannel);
        }
        LinuxDdc.WriteFrame(I2cTransfer.DevicePath(environment.DeviceRoot, bus), DdcFrame.SetInput(input.Value, input.Protocol));
    }

    private void OpenSettings()
    {
        store.EnsureFileExists();
        var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        start.ArgumentList.Add(store.FilePath);
        Process.Start(start)?.Dispose();
    }

    private void ToggleAutostart()
    {
        if (DesktopEntry.IsInstalled(environment.AutostartFile))
        {
            DesktopEntry.Remove(environment.AutostartFile);
        }
        else
        {
            DesktopEntry.Write(environment.AutostartFile, environment.Executable);
        }
    }

    private void Report(string title, Action action)
    {
        try
        {
            action();
        }
        catch (SettingsException exception)
        {
            _ = notifier.ShowAsync(title, AppText.InvalidSettings(exception.Error));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            _ = notifier.ShowAsync(title, exception.Message);
        }
    }
}
