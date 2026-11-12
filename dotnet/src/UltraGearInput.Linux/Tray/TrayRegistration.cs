using UltraGearInput.Linux.DBus;

namespace UltraGearInput.Linux.Tray;

internal sealed class TrayRegistration(Func<CancellationToken, Task> register, TextWriter log, TimeSpan timeout)
{
    private int running;

    public async Task<bool> RegisterAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            return false;
        }
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(timeout);
                try
                {
                    await register(deadline.Token).ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception exception) when (exception is DBusException or OperationCanceledException)
                {
                    await log.WriteLineAsync($"Tray registration attempt {attempt + 1} failed: {exception.Message}").ConfigureAwait(false);
                }
            }
            return false;
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }
}
