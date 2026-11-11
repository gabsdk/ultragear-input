using UltraGearInput.Core;
using UltraGearInput.Linux;
using UltraGearInput.Linux.Hardware;
using UltraGearInput.Linux.Tray;

namespace UltraGearInput.Tests.Linux;

public sealed class LinuxPlatformTests
{
    [Fact]
    public void ResolvesRelativeSymlinksPhysically()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateDirectory("devices", "gpu", "i2c-3");
        directory.CreateDirectory("class");
        directory.Link(Path.Combine("..", "devices", "gpu", "i2c-3"), "class", "i2c-3");
        directory.Link("..", "devices", "gpu", "i2c-3", "device");

        var resolved = PathResolver.RealPath(directory.Combine("class", "i2c-3", "device"));

        Assert.Equal(PathResolver.RealPath(directory.Combine("devices", "gpu")), resolved);
    }

    [Fact]
    public void StopsOnSymlinkLoops()
    {
        using var directory = new TemporaryDirectory();
        directory.Link("b", "a");
        directory.Link("a", "b");
        Assert.Throws<IOException>(() => PathResolver.RealPath(directory.Combine("a")));
    }

    [Theory]
    [InlineData(0x08000001UL, "PlainWrite")]
    [InlineData(0x08000000UL, "SmbusI2cBlock")]
    [InlineData(0x04000000UL, "Unsupported")]
    [InlineData(0UL, "Unsupported")]
    public void ChoosesTheWriteMethodFromAdapterFunctionality(ulong functionality, string expected) =>
        Assert.Equal(expected, I2cTransfer.WriteMethod((I2cFunctionality)functionality).ToString());

    [Theory]
    [InlineData(Errno.NoEntry, "DeviceMissing")]
    [InlineData(Errno.NoDevice, "DeviceMissing")]
    [InlineData(Errno.AccessDenied, "PermissionDenied")]
    [InlineData(Errno.NoPermission, "PermissionDenied")]
    [InlineData(Errno.NotTty, "Unsupported")]
    [InlineData(Errno.RemoteIo, "Rejected")]
    public void MapsErrnoToSwitchFailures(int errno, string expected)
    {
        var failure = SwitchException.FromErrno(new I2cException("/dev/i2c-4", errno));
        Assert.Equal((expected, "/dev/i2c-4", errno), (failure.Reason.ToString(), failure.DevicePath, failure.Errno));
    }

    [Fact]
    public void DescribesEveryFailureForThePerson()
    {
        foreach (var reason in Enum.GetValues<SwitchFailure>())
        {
            var text = LinuxText.Describe(new SwitchException(reason, "/dev/i2c-4", 121));
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain(reason.ToString(), text, StringComparison.Ordinal);
        }
        Assert.Contains("/dev/i2c-4", LinuxText.Describe(new SwitchException(SwitchFailure.PermissionDenied, "/dev/i2c-4")), StringComparison.Ordinal);
        Assert.Contains("errno 121", LinuxText.Describe(new SwitchException(SwitchFailure.Rejected, "/dev/i2c-4", 121)), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildsDevicePaths()
    {
        using var directory = new TemporaryDirectory();
        Assert.Equal(directory.Combine("i2c-7"), I2cTransfer.DevicePath(directory.Path, 7));
    }

    [Fact]
    public unsafe void SmbusIoctlStructureMatchesTheKernelLayout()
    {
        var data = default(I2cSmbusIoctlData);
        Assert.Equal(16, sizeof(I2cSmbusIoctlData));
        Assert.Equal(1, (byte*)&data.Command - (byte*)&data);
        Assert.Equal(4, (byte*)&data.Size - (byte*)&data);
        Assert.Equal(8, (byte*)&data.Data - (byte*)&data);
    }

    [Fact]
    public void EnvironmentFollowsXdgAndOverrides()
    {
        var defaults = LinuxEnvironment.From(_ => null, "/home/ana", "/opt/ugi/ultragear-input");
        Assert.Equal(("/sys", "/dev"), (defaults.SysfsRoot, defaults.DeviceRoot));
        Assert.Equal(Path.Combine("/home/ana", ".config", "ultragear-input", "settings.json"), defaults.SettingsFile);
        Assert.Equal(Path.Combine("/home/ana", ".config", "autostart", "ultragear-input.desktop"), defaults.AutostartFile);
        Assert.Equal(Path.Combine("/home/ana", ".local", "share", "applications", "ultragear-input.desktop"), defaults.LauncherFile);

        var custom = LinuxEnvironment.From(
            name => name switch
            {
                "XDG_CONFIG_HOME" => "/cfg",
                "XDG_DATA_HOME" => "/data",
                LinuxEnvironment.SysfsVariable => "/fake/sys",
                LinuxEnvironment.DeviceVariable => "/fake/dev",
                _ => " ",
            },
            "/home/ana",
            "/x");
        Assert.Equal(("/fake/sys", "/fake/dev", Path.Combine("/cfg", "ultragear-input", "settings.json"), Path.Combine("/data", "applications", "ultragear-input.desktop")),
            (custom.SysfsRoot, custom.DeviceRoot, custom.SettingsFile, custom.LauncherFile));
    }

    [Fact]
    public void DesktopEntryQuotesTheExecutable()
    {
        Assert.Equal("\"/opt/my apps/ultragear-input\"", DesktopEntry.QuoteExec("/opt/my apps/ultragear-input"));
        Assert.Equal("\"/a\\$b\\\"c%%\"", DesktopEntry.QuoteExec("/a$b\"c%"));
        var entry = DesktopEntry.Render("/home/ana/.local/bin/ultragear-input");
        Assert.StartsWith("[Desktop Entry]\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nExec=\"/home/ana/.local/bin/ultragear-input\"\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nName=UltraGear Input\n", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopEntryWritesAndRemovesFiles()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.Combine("autostart", "ultragear-input.desktop");
        Assert.False(DesktopEntry.IsInstalled(file));
        DesktopEntry.Write(file, "/bin/ultragear-input");
        Assert.True(DesktopEntry.IsInstalled(file));
        Assert.Equal(DesktopEntry.Render("/bin/ultragear-input"), File.ReadAllText(file));
        DesktopEntry.Remove(file);
        DesktopEntry.Remove(file);
        Assert.False(DesktopEntry.IsInstalled(file));
    }

    [Fact]
    public void IconPixmapsAreOpaqueArgbSquares()
    {
        foreach (var size in TrayIconPixmap.Sizes)
        {
            var pixels = TrayIconPixmap.Render(size);
            Assert.Equal(size * size * 4, pixels.Length);
            var opaque = Enumerable.Range(0, size * size).Count(index => pixels[index * 4] == 0xFF);
            Assert.InRange(opaque, size, size * size / 2);
            Assert.All(Enumerable.Range(0, size * size).Where(index => pixels[index * 4] == 0xFF), index => Assert.Equal(0xBE, pixels[(index * 4) + 1]));
        }
    }

    [Fact]
    public async Task NotificationsAreFireAndForget()
    {
        var sender = new RecordingSender();
        using var log = new StringWriter();
        await new DesktopNotifier(sender, log).ShowAsync("Title", "Body");
        Assert.Equal("Title: Body" + Environment.NewLine, log.ToString());
        var call = Assert.Single(sender.Sent);
        Assert.False(call.ExpectsReply);
        Assert.Equal(("Notify", DesktopNotifier.Name, "susssasa{sv}i"), (call.Member!, call.Destination!, call.Signature));
        Assert.Equal(AppText.AppName, call.Body[0]);
        Assert.Equal(("Title", "Body"), ((string)call.Body[3], (string)call.Body[4]));
    }
}
