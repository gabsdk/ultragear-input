using UltraGearInput.Core;
using UltraGearInput.Linux.DBus;
using UltraGearInput.Linux.Tray;

namespace UltraGearInput.Tests.Linux;

public sealed class DBusMenuModelTests
{
    [Fact]
    public void BuildsTheLayoutTreeWithSubmenusAndToggles()
    {
        var model = new DBusMenuModel();
        Assert.Equal(MenuChangeKind.Layout, model.Update(Displays.Menu(Displays.Lg)).Kind);

        var root = model.Layout(DBusMenuModel.RootId, -1, []);
        var children = Children(root);
        Assert.Equal(
            ["LG ULTRAGEAR", "DisplayPort 1", "DisplayPort 2 / USB-C", "USB-C / DisplayPort 3", "HDMI 1", "HDMI 2", "Protocol", null, AppText.OpenSettings, "Start at Login", null, UltraGearInput.Core.Updates.UpdateText.Get("check"), "Quit"],
            children.Select(child => Label(child)));

        Assert.False((bool)Properties(children[0])["enabled"].Value);
        Assert.Equal("separator", Properties(children[7])["type"].Value);
        Assert.Equal("submenu", Properties(children[6])["children-display"].Value);
        var choices = Children(children[6]);
        Assert.Equal(["Standard DDC/CI", "LG"], choices.Select(choice => Label(choice)));
        Assert.Equal([0, 1], choices.Select(choice => (int)Properties(choice)["toggle-state"].Value));
        Assert.All(choices, choice => Assert.Equal("radio", Properties(choice)["toggle-type"].Value));
        Assert.Equal(("checkmark", 0), ((string)Properties(children[9])["toggle-type"].Value, (int)Properties(children[9])["toggle-state"].Value));
    }

    [Fact]
    public void MapsItemIdsToTheirActions()
    {
        var model = new DBusMenuModel();
        model.Update(Displays.Menu(Displays.Lg));
        var children = Children(model.Layout(DBusMenuModel.RootId, -1, []));

        var hdmi = Assert.IsType<TrayInput>(model.ActionFor((int)children[4][0]));
        Assert.Equal((Displays.Lg.Key, (ushort)0x90, DdcProtocol.Lg), (hdmi.Display.Key, hdmi.Value, hdmi.Protocol));
        var standard = Assert.IsType<TrayProtocolChoice>(model.ActionFor((int)Children(children[6])[0][0]));
        Assert.Equal(DdcProtocol.Standard, standard.Protocol);
        Assert.Equal(TrayCommandKind.Quit, Assert.IsType<TrayCommand>(model.ActionFor((int)children[12][0])).Kind);
        Assert.Null(model.ActionFor((int)children[0][0]));
        Assert.Null(model.ActionFor(999));
    }

    [Fact]
    public void RevisionOnlyChangesWhenTheLayoutChanges()
    {
        var model = new DBusMenuModel();
        Assert.Equal(MenuChangeKind.Layout, model.Update(Displays.Menu(Displays.Lg)).Kind);
        var revision = model.Revision;
        Assert.Equal(MenuChange.None, model.Update(Displays.Menu(Displays.Lg)));
        Assert.Equal(revision, model.Revision);
        Assert.Equal(MenuChangeKind.Layout, model.Update(Displays.Menu(Displays.Lg, Displays.Dell)).Kind);
        Assert.Equal(revision + 1, model.Revision);
    }

    [Fact]
    public void ReportsToggleAndLabelChangesAsPropertyUpdates()
    {
        var model = new DBusMenuModel();
        model.Update(Displays.Menu(Displays.Lg));
        var revision = model.Revision;
        var loaded = new SettingsLoad(new Settings { Protocols = new Dictionary<string, DdcProtocol> { [Displays.Lg.Key] = DdcProtocol.Standard } }, null);

        var change = model.Update(TrayMenu.Build([Displays.Lg], loaded, new TrayCommandLabels("Start at Login", "Quit"), startsAtLogin: true));

        Assert.Equal(MenuChangeKind.Properties, change.Kind);
        Assert.Equal(revision, model.Revision);
        var ids = change.Updated.Cast<object[]>().Select(item => (int)item[0]).ToList();
        var children = Children(model.Layout(DBusMenuModel.RootId, -1, []));
        var choices = Children(children[6]);
        Assert.Equal([(int)choices[0][0], (int)choices[1][0], (int)children[9][0]], ids);
        Assert.Equal(1, (int)Properties(choices[0])["toggle-state"].Value);
        Assert.Empty(change.Removed);
    }

    [Fact]
    public void ReportsRemovedProperties()
    {
        var model = new DBusMenuModel();
        model.Update([new TrayNotice("Busy")]);
        var change = model.Update([new TrayCommand(TrayCommandKind.Quit, "Busy")]);
        Assert.Equal(MenuChangeKind.Properties, change.Kind);
        var removed = (object[])Assert.Single(change.Removed);
        Assert.Equal(["enabled"], (IEnumerable<object>)removed[1]);
    }

    [Fact]
    public void RespectsRecursionDepthAndPropertyFilters()
    {
        var model = new DBusMenuModel();
        model.Update(Displays.Menu(Displays.Lg));
        Assert.Empty(Children(model.Layout(DBusMenuModel.RootId, 0, [])));
        var shallow = Children(model.Layout(DBusMenuModel.RootId, 1, []));
        Assert.Empty(Children(shallow[6]));
        var filtered = Children(model.Layout(DBusMenuModel.RootId, 1, ["enabled"]));
        Assert.Equal(["enabled"], Properties(filtered[0]).Keys);
        Assert.Empty(Properties(filtered[1]));
    }

    [Fact]
    public void ReturnsGroupPropertiesAndSingleProperties()
    {
        var model = new DBusMenuModel();
        model.Update(Displays.Menu(Displays.Lg));
        var group = model.GroupProperties([1, 2, 999], ["label"]);
        Assert.Equal([1, 2], group.Cast<object[]>().Select(item => (int)item[0]));
        Assert.Equal("LG ULTRAGEAR", model.Property(1, "label").Value);
        Assert.Throws<DBusException>(() => model.Property(1, "missing"));
        Assert.Throws<DBusException>(() => model.Layout(999, -1, []));
    }

    [Fact]
    public void EscapesMnemonicUnderscores() => Assert.Equal("HDMI__1", DBusMenuModel.EscapeLabel("HDMI_1"));

    [Fact]
    public void LayoutEncodesWithTheDBusMenuSignature()
    {
        var model = new DBusMenuModel();
        model.Update(Displays.Menu(Displays.Lg, Displays.Dell));
        var writer = new DBusWriter();
        writer.Write("u" + DBusMenuModel.NodeSignature, [model.Revision, model.Layout(DBusMenuModel.RootId, -1, [])]);
        var decoded = new DBusReader(writer.ToArray(), false).Read("u" + DBusMenuModel.NodeSignature);
        Assert.Equal(model.Revision, decoded[0]);
        Assert.Equal(Children(model.Layout(0, -1, [])).Count, Children((object[])decoded[1]).Count);
    }

    internal static List<object[]> Children(object[] node) =>
        [.. ((IEnumerable<object>)node[2]).Select(child => (object[])((DBusVariant)child).Value)];

    internal static Dictionary<string, DBusVariant> Properties(object[] node) =>
        ((IEnumerable<object>)node[1]).Cast<DBusDictEntry>().ToDictionary(entry => (string)entry.Key, entry => (DBusVariant)entry.Value);

    internal static string? Label(object[] node) =>
        Properties(node).TryGetValue("label", out var label) ? (string)label.Value : null;
}
