using UltraGearInput.Core;

namespace UltraGearInput.Tests;

public sealed class EdidAndMenuTests
{
    [Fact]
    public void ParsesEdidVectors()
    {
        foreach (var vector in ProtocolSpec.Shared.Edids)
        {
            var edid = Edid.Parse(vector.Bytes);
            Assert.Equal(new Edid(vector.ManufacturerCode, vector.ProductCode, vector.SerialNumber, vector.ProductName), edid);
            Assert.Equal(vector.Identity, Edid.IdentityOf(vector.Bytes));
        }
    }

    [Fact]
    public void RejectsBlocksWithoutTheEdidHeader()
    {
        var bytes = ProtocolSpec.Shared.Lg.Bytes;
        bytes[1] = 0;
        Assert.Null(Edid.Parse(bytes));
        Assert.Null(Edid.Parse(ProtocolSpec.Shared.Lg.Bytes.AsSpan(0, 100)));
    }

    [Fact]
    public void ToleratesEdidsWithoutAProductName()
    {
        var bytes = ProtocolSpec.Shared.Lg.Bytes;
        bytes[75] = 0xFE;
        Assert.Null(Edid.Parse(bytes)!.ProductName);
    }

    [Fact]
    public void ComparesOnlyTheBaseBlock()
    {
        var lg = ProtocolSpec.Shared.Lg.Bytes;
        Assert.True(Edid.SameBaseBlock(lg, [.. lg, .. new byte[128]]));
        Assert.False(Edid.SameBaseBlock(lg, ProtocolSpec.Shared.Dell.Bytes));
    }

    [Fact]
    public void BuildsTheMenuInDisplayOrder()
    {
        var entries = Displays.Menu(Displays.Lg, Displays.Dell);
        Assert.IsType<TrayHeader>(entries[0]);
        var dellHeader = entries.OfType<TrayHeader>().Last();
        Assert.Equal("DELL U2723QE", dellHeader.Text);
        Assert.IsType<TraySeparator>(entries[entries.ToList().IndexOf(dellHeader) - 1]);
        Assert.All(entries.OfType<TrayInput>().Where(input => input.Display == Displays.Dell), input => Assert.Equal(DdcProtocol.Standard, input.Protocol));
        Assert.Equal([TrayCommandKind.OpenSettings, TrayCommandKind.StartAtLogin, TrayCommandKind.Update, TrayCommandKind.Quit], entries.OfType<TrayCommand>().Select(command => command.Kind));
    }

    [Fact]
    public void ExplainsAnEmptyMenuAndInvalidSettings()
    {
        var entries = TrayMenu.Build([], new SettingsLoad(new Settings(), SettingsError.DuplicateInputId), new TrayCommandLabels("Start", "Quit"), startsAtLogin: true);
        Assert.Equal(AppText.NoDisplays, Assert.IsType<TrayNotice>(entries[0]).Text);
        Assert.Contains(entries, entry => entry is TrayNotice notice && notice.Text == AppText.InvalidSettings(SettingsError.DuplicateInputId));
        Assert.True(entries.OfType<TrayCommand>().Single(command => command.Kind == TrayCommandKind.StartAtLogin).IsChecked);
    }

    [Fact]
    public void UsesSavedProtocolAndCustomInputs()
    {
        var settings = new Settings
        {
            Inputs = [new InputSource("ps5", "PlayStation", 17, 144)],
            Protocols = new Dictionary<string, DdcProtocol> { [Displays.Lg.Key] = DdcProtocol.Standard },
        };
        var entries = TrayMenu.Build([Displays.Lg], new SettingsLoad(settings, null), new TrayCommandLabels("Start", "Quit"), false);
        var input = Assert.Single(entries.OfType<TrayInput>());
        Assert.Equal(("PlayStation", (ushort)17), (input.Input.Name, input.Value));
        Assert.Equal(DdcProtocol.Standard, entries.OfType<TrayProtocolMenu>().Single().Choices.Single(choice => choice.IsSelected).Protocol);
    }

    [Fact]
    public void SettingsStoreSavesProtocolsAndRefusesInvalidFiles()
    {
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.Combine("config", "settings.json"));
        Assert.Equal(new SettingsLoad(new Settings(), null).Error, store.Load().Error);

        store.SaveProtocol(DdcProtocol.Standard, Displays.Lg);
        Assert.Equal(DdcProtocol.Standard, store.Load().Settings.ProtocolFor(Displays.Lg));

        File.WriteAllText(store.FilePath, "{broken");
        Assert.Equal(SettingsError.Malformed, store.Load().Error);
        Assert.Equal(SettingsError.Malformed, Assert.Throws<SettingsException>(() => store.SaveProtocol(DdcProtocol.Lg, Displays.Lg)).Error);
        Assert.Equal("{broken", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void EnsureFileExistsWritesDefaultsOnce()
    {
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.Combine("settings.json"));
        store.EnsureFileExists();
        Assert.Equal(InputSource.Defaults, store.Load().Settings.Inputs);
        File.WriteAllText(store.FilePath, "{}");
        store.EnsureFileExists();
        Assert.Equal("{}", File.ReadAllText(store.FilePath));
    }
}
