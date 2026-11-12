using System.Formats.Tar;
using System.IO.Compression;
using UltraGearInput.Core.Updates;

namespace UltraGearInput.Tests.Updates;

public sealed class UpdateInstallerTests
{
    [Theory]
    [InlineData("payload")]
    [InlineData("./payload")]
    public void NormalizesTheOwnedPayloadBeforeValidation(string payload)
    {
        using var directory = new TemporaryDirectory();
        var plan = new UpdatePlan(directory.Combine("installed app"), directory.Combine(payload), "", 1, 0, "2.3.4");
        var normalized = UpdateInstaller.ValidatePlanPaths(plan, directory.Path);
        Assert.Equal(directory.Combine("payload"), normalized.Payload);
        Assert.Equal(plan.Target, normalized.Target);
    }

    [Theory]
    [InlineData("other/payload", "installed", 1)]
    [InlineData("relative:payload", "installed", 1)]
    [InlineData("../payload", "installed", 1)]
    [InlineData("payload", "relative:installed", 1)]
    [InlineData("payload", "installed", 0)]
    public void RejectsPathsOutsideTheStageOrInvalidOwnership(string payload, string target, int parent)
    {
        using var directory = new TemporaryDirectory();
        var plan = new UpdatePlan(target.StartsWith("relative:", StringComparison.Ordinal) ? target[9..] : directory.Combine(target),
            payload.StartsWith("relative:", StringComparison.Ordinal) ? payload[9..] : directory.Combine(payload), "", parent, 0, "2.3.4");
        Assert.Throws<UpdateException>(() => UpdateInstaller.ValidatePlanPaths(plan, directory.Path));
    }

    [Fact]
    public async Task ReplacesOnlyTheExecutableAndPreservesItsPermissions()
    {
        using var directory = new TemporaryDirectory();
        var target = directory.Combine("installed app");
        var payload = directory.Combine("payload");
        File.WriteAllText(target, "previous");
        File.WriteAllText(payload, "next");
        var settings = directory.Combine("settings.json");
        File.WriteAllText(settings, "keep");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var restarted = false;
        await UpdateInstaller.ReplaceAsync(await Plan(target, payload), TestContext.Current.CancellationToken, () => { restarted = true; return Task.CompletedTask; });
        Assert.Equal("next", File.ReadAllText(target));
        Assert.Equal("keep", File.ReadAllText(settings));
        Assert.True(restarted);
        Assert.Equal(["installed app", "payload", "settings.json"], Directory.EnumerateFiles(directory.Path).Select(Path.GetFileName).Order());
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(target).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task RestoresThePreviousAppWhenRestartFails()
    {
        using var directory = new TemporaryDirectory();
        var target = directory.Combine("installed");
        var payload = directory.Combine("payload");
        File.WriteAllText(target, "previous");
        File.WriteAllText(payload, "next");
        var plan = await Plan(target, payload);
        await Assert.ThrowsAsync<IOException>(() => UpdateInstaller.ReplaceAsync(plan, TestContext.Current.CancellationToken, () => throw new IOException("test")));
        Assert.Equal("previous", File.ReadAllText(target));
        Assert.DoesNotContain(Directory.EnumerateFiles(directory.Path), path => path.Contains(".new-", StringComparison.Ordinal) || path.Contains(".previous-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AChangedPayloadNeverReplacesTheInstalledApp()
    {
        using var directory = new TemporaryDirectory();
        var target = directory.Combine("installed");
        var payload = directory.Combine("payload");
        File.WriteAllText(target, "previous");
        File.WriteAllText(payload, "next");
        var plan = await Plan(target, payload);
        File.WriteAllText(payload, "tampered");
        await Assert.ThrowsAsync<UpdateException>(() => UpdateInstaller.ReplaceAsync(plan, TestContext.Current.CancellationToken));
        Assert.Equal("previous", File.ReadAllText(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractsOnlyTheExpectedLinuxBinaryAndRejectsSymlinks(bool symbolic)
    {
        using var directory = new TemporaryDirectory();
        var archive = directory.Combine("archive.tar.gz");
        var release = UpdateRelease.Parse(ReleaseTests.Fixture(), UpdateTarget.LinuxArm64);
        var header = new byte[64];
        new byte[] { 0x7F, 0x45, 0x4C, 0x46, 2, 1 }.CopyTo(header, 0);
        header[18] = 183;
        await using (var stream = File.Create(archive))
        await using (var compressed = new GZipStream(stream, CompressionMode.Compress))
        await using (var writer = new TarWriter(compressed))
        {
            var entry = new PaxTarEntry(symbolic ? TarEntryType.SymbolicLink : TarEntryType.RegularFile, "ultragear-input-2.3.4-linux-arm64/ultragear-input");
            if (symbolic)
            {
                entry.LinkName = "/etc/passwd";
            }
            else
            {
                entry.DataStream = new MemoryStream(header);
            }
            await writer.WriteEntryAsync(entry, TestContext.Current.CancellationToken);
        }
        var destination = directory.Combine("payload");
        if (symbolic)
        {
            await Assert.ThrowsAsync<UpdateException>(() => UpdateInstaller.ExtractAsync(archive, destination, release, UpdateTarget.LinuxArm64, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(destination));
        }
        else
        {
            await UpdateInstaller.ExtractAsync(archive, destination, release, UpdateTarget.LinuxArm64, TestContext.Current.CancellationToken);
            Assert.Equal(header, File.ReadAllBytes(destination));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CleansOnlyAnOwnedCompletedUpdate(bool matchingTarget)
    {
        var stage = Directory.CreateTempSubdirectory("ultragear-update-").FullName;
        try
        {
            var plan = new UpdatePlan(matchingTarget ? Environment.ProcessPath! : "/different-app", Path.Combine(stage, "payload"), "", 1, 0, "2.3.4");
            await File.WriteAllTextAsync(Path.Combine(stage, "plan.json"), System.Text.Json.JsonSerializer.Serialize(plan, UpdatePlanJsonContext.Default.UpdatePlan), TestContext.Current.CancellationToken);
            await UpdateInstaller.CleanupAsync(stage, int.MaxValue);
            Assert.Equal(!matchingTarget, Directory.Exists(stage));
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    private static async Task<UpdatePlan> Plan(string target, string payload) =>
        new(target, payload, await UpdateInstaller.HashAsync(payload, TestContext.Current.CancellationToken), 1, 0, "2.3.4", false);
}
