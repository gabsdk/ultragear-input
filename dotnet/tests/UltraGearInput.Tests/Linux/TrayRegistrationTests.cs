using UltraGearInput.Linux.DBus;
using UltraGearInput.Linux.Tray;

namespace UltraGearInput.Tests.Linux;

public sealed class TrayRegistrationTests
{
    [Fact]
    public async Task RetriesARegistrationThatNeverReplies()
    {
        var attempts = 0;
        var registration = new TrayRegistration(async token =>
        {
            if (++attempts == 1)
            {
                await Task.Delay(Timeout.Infinite, token);
            }
        }, TextWriter.Null, TimeSpan.FromMilliseconds(20));
        Assert.True(await registration.RegisterAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task BoundsFailuresAndAllowsAReplacementWatcher()
    {
        var attempts = 0;
        var ready = false;
        var registration = new TrayRegistration(_ =>
        {
            attempts++;
            return ready ? Task.CompletedTask : Task.FromException(new DBusException("org.test.NotReady", "not ready"));
        }, TextWriter.Null, TimeSpan.FromSeconds(1));
        Assert.False(await registration.RegisterAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, attempts);
        ready = true;
        Assert.True(await registration.RegisterAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreventsOverlappingRegistrationsAndStopsAfterCancellation()
    {
        using var stop = new CancellationTokenSource();
        var attempts = 0;
        var registration = new TrayRegistration(token =>
        {
            attempts++;
            return Task.Delay(Timeout.Infinite, token);
        }, TextWriter.Null, TimeSpan.FromSeconds(10));
        var pending = registration.RegisterAsync(stop.Token);
        Assert.False(await registration.RegisterAsync(stop.Token));
        await stop.CancelAsync();
        Assert.False(await pending);
        Assert.Equal(1, attempts);
    }
}
