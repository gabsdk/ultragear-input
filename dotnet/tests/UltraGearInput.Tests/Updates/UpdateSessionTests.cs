using UltraGearInput.Core.Updates;

namespace UltraGearInput.Tests.Updates;

public sealed class UpdateSessionTests
{
    [Fact]
    public async Task ChecksFirstAndInstallsOnlyOnTheSecondExplicitClick()
    {
        var installed = 0;
        var quit = false;
        var notices = new List<string>();
        var release = UpdateRelease.Parse(ReleaseTests.Fixture(), UpdateTarget.WindowsX64);
        using var session = new UpdateSession(_ => Task.FromResult<UpdateRelease?>(release), (_, _) => { installed++; return Task.CompletedTask; }, notices.Add, () => quit = true, "1.0.0");
        Assert.Equal(0, installed);
        await session.ExecuteAsync();
        Assert.Equal(0, installed);
        Assert.False(session.IsBusy);
        Assert.Contains("2.3.4", session.Label, StringComparison.Ordinal);
        await session.ExecuteAsync();
        Assert.Equal(1, installed);
        Assert.True(quit);
        Assert.Equal(2, notices.Count);
    }

    [Fact]
    public async Task CoalescesConcurrentClicksAndDoesNotInstallAfterDispose()
    {
        var completion = new TaskCompletionSource<UpdateRelease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkedCount = 0;
        var notices = 0;
        using var session = new UpdateSession(_ => { checkedCount++; return completion.Task; }, (_, _) => throw new InvalidOperationException(), _ => notices++, () => throw new InvalidOperationException(), "1.0.0");
        var first = session.ExecuteAsync();
        await session.ExecuteAsync();
        Assert.True(session.IsBusy);
        Assert.Equal(1, checkedCount);
        session.Dispose();
        completion.SetResult(UpdateRelease.Parse(ReleaseTests.Fixture(), UpdateTarget.WindowsX64));
        await first;
        Assert.Equal(0, notices);
        await session.ExecuteAsync();
        Assert.Equal(1, checkedCount);
    }

    [Fact]
    public async Task NetworkFailureRestoresAnActionableMenuAndReportsTheError()
    {
        var notices = new List<string>();
        using var session = new UpdateSession(_ => throw new HttpRequestException(), (_, _) => Task.CompletedTask, notices.Add, () => throw new InvalidOperationException(), "1.0.0");
        await session.ExecuteAsync();
        Assert.False(session.IsBusy);
        Assert.Equal(UpdateText.Get("check"), session.Label);
        Assert.Equal(UpdateText.Get("network"), Assert.Single(notices));
    }
    [Fact]
    public async Task UpToDateDoesNotEnableInstallation()
    {
        var notices = new List<string>();
        using var session = new UpdateSession(_ => Task.FromResult<UpdateRelease?>(null), (_, _) => throw new InvalidOperationException(), notices.Add, () => throw new InvalidOperationException(), "2.3.4");
        await session.ExecuteAsync();
        Assert.Equal(UpdateText.Get("check"), session.Label);
        Assert.Equal(UpdateText.Get("current", value: "2.3.4"), Assert.Single(notices));
    }

    [Fact]
    public async Task InstallationFailureKeepsTheOfferAndTheAppRunning()
    {
        var notices = new List<string>();
        var quits = 0;
        var release = UpdateRelease.Parse(ReleaseTests.Fixture(), UpdateTarget.WindowsX64);
        using var session = new UpdateSession(_ => Task.FromResult<UpdateRelease?>(release), (_, _) => throw new UpdateException("checksum"), notices.Add, () => quits++, "1.0.0");
        await session.ExecuteAsync();
        await session.ExecuteAsync();
        Assert.False(session.IsBusy);
        Assert.Contains("2.3.4", session.Label, StringComparison.Ordinal);
        Assert.Equal(0, quits);
        Assert.Equal(UpdateText.Get("checksum"), notices.Last());
    }

}
