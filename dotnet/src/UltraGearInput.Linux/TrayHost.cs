using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using UltraGearInput.Core;
using UltraGearInput.Linux.DBus;
using UltraGearInput.Linux.Hardware;
using UltraGearInput.Linux.Tray;

namespace UltraGearInput.Linux;

[SupportedOSPlatform("linux")]
internal sealed class TrayHost(LinuxEnvironment environment, DisplayInventory inventory, SettingsStore store)
{
    public const string InstanceName = "dev.gabsdk.UltraGearInput";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TrayHostGrace = TimeSpan.FromSeconds(15);

    public async Task<int> RunAsync(DBusEndpoint endpoint)
    {
        using var stop = new CancellationTokenSource();
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => Cancel(context, stop));
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => Cancel(context, stop));

        await using var connection = await DBusConnection.ConnectAsync(endpoint, LibC.GetEffectiveUserId(), stop.Token).ConfigureAwait(false);
        var notifier = new DesktopNotifier(connection, Console.Error);
        using var backend = new LinuxTrayBackend(environment, inventory, store, notifier, stop.Cancel);
        var service = new TrayService(connection, backend);
        var reader = connection.RunAsync(service, stop.Token);

        await connection.HelloAsync(stop.Token).ConfigureAwait(false);
        if (!await connection.RequestNameAsync(InstanceName, stop.Token).ConfigureAwait(false))
        {
            await Console.Error.WriteLineAsync(LinuxText.AlreadyRunning).ConfigureAwait(false);
            return 0;
        }
        var itemName = string.Format(CultureInfo.InvariantCulture, "org.kde.StatusNotifierItem-{0}-1", Environment.ProcessId);
        await connection.RequestNameAsync(itemName, stop.Token).ConfigureAwait(false);
        await service.RefreshAsync().ConfigureAwait(false);

        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.WatcherChanged += present =>
        {
            if (present)
            {
                _ = RegisterAsync(connection, itemName, registered, stop.Token);
            }
        };
        await connection.AddMatchAsync(DBusBus.NameOwnerChangedRule(TrayService.WatcherName), stop.Token).ConfigureAwait(false);
        if (await connection.NameHasOwnerAsync(TrayService.WatcherName, stop.Token).ConfigureAwait(false))
        {
            await RegisterAsync(connection, itemName, registered, stop.Token).ConfigureAwait(false);
        }
        _ = WarnWithoutTrayHostAsync(notifier, registered.Task, stop.Token);

        var refresher = RefreshLoopAsync(service, stop.Token);
        var finished = await Task.WhenAny(reader, refresher, Task.Delay(Timeout.Infinite, stop.Token)).ConfigureAwait(false);
        var exitCode = stop.IsCancellationRequested ? 0 : 1;
        if (exitCode != 0)
        {
            await Console.Error.WriteLineAsync($"Stopped because the {(finished == reader ? "D-Bus connection" : "refresh loop")} ended.").ConfigureAwait(false);
        }
        await stop.CancelAsync().ConfigureAwait(false);
        return exitCode;
    }

    private static async Task RegisterAsync(DBusConnection connection, string itemName, TaskCompletionSource registered, CancellationToken cancellationToken)
    {
        try
        {
            await connection.CallAsync(
                DBusMessage.MethodCall(TrayService.WatcherName, TrayService.WatcherPath, TrayService.WatcherName, "RegisterStatusNotifierItem", "s", itemName),
                cancellationToken).ConfigureAwait(false);
            registered.TrySetResult();
            await Console.Error.WriteLineAsync($"Registered {itemName} with {TrayService.WatcherName}.").ConfigureAwait(false);
        }
        catch (DBusException exception)
        {
            await Console.Error.WriteLineAsync($"Tray registration failed: {exception.Name}: {exception.Message}").ConfigureAwait(false);
        }
    }

    private static async Task WarnWithoutTrayHostAsync(DesktopNotifier notifier, Task registered, CancellationToken cancellationToken)
    {
        var first = await Task.WhenAny(registered, Task.Delay(TrayHostGrace, cancellationToken)).ConfigureAwait(false);
        if (first != registered && !cancellationToken.IsCancellationRequested)
        {
            await notifier.ShowAsync(LinuxText.NoTrayHostTitle, LinuxText.NoTrayHostBody).ConfigureAwait(false);
        }
    }

    private static async Task RefreshLoopAsync(TrayService service, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await service.RefreshAsync().ConfigureAwait(false);
        }
    }

    private static void Cancel(PosixSignalContext context, CancellationTokenSource stop)
    {
        context.Cancel = true;
        stop.Cancel();
    }
}
