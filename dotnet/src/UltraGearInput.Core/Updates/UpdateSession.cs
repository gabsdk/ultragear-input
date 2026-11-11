namespace UltraGearInput.Core.Updates;

public sealed class UpdateSession(
    Func<CancellationToken, Task<UpdateRelease?>> check,
    Func<UpdateRelease, CancellationToken, Task> install,
    Action<string> notify,
    Action quit,
    string currentVersion) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private UpdateRelease? offer;
    private int busy;

    public event Action? Changed;

    public bool IsBusy => Volatile.Read(ref busy) != 0;

    public string Label => UpdateText.Get(IsBusy ? offer is null ? "checking" : "installing" : offer is null ? "check" : "install", value: offer?.Version.ToString());

    public async Task ExecuteAsync()
    {
        if (lifetime.IsCancellationRequested || Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        {
            return;
        }
        Changed?.Invoke();
        try
        {
            if (offer is null)
            {
                var latest = await check(lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                if (latest is not null && latest.Version.CompareTo(ReleaseVersion.Parse(currentVersion)) > 0)
                {
                    offer = latest;
                    notify(UpdateText.Get("available", value: latest.Version.ToString()));
                }
                else
                {
                    notify(UpdateText.Get("current", value: currentVersion));
                }
            }
            else
            {
                await install(offer, lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                notify(UpdateText.Get("restarting"));
                quit();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is UpdateException or HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (!lifetime.IsCancellationRequested)
            {
                notify(UpdateText.Get(FailureKey(exception)));
            }
        }
        finally
        {
            Volatile.Write(ref busy, 0);
            if (!lifetime.IsCancellationRequested)
            {
                Changed?.Invoke();
            }
        }
    }

    public void Dispose() => lifetime.Cancel();

    private static string FailureKey(Exception exception) => exception switch
    {
        UpdateException error => error.Reason,
        System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException => "release",
        UnauthorizedAccessException => "permission",
        InvalidDataException => "package",
        HttpRequestException or OperationCanceledException => "network",
        _ => "installFailed",
    };
}
