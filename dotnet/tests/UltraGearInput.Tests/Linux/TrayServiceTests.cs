using System.Xml.Linq;
using UltraGearInput.Core;
using UltraGearInput.Linux.DBus;
using UltraGearInput.Linux.Tray;

namespace UltraGearInput.Tests.Linux;

public sealed class TrayServiceTests
{
    private readonly RecordingSender sender = new();
    private readonly FakeBackend backend = new() { Menu = Displays.Menu(Displays.Lg) };
    private readonly TrayService service;

    public TrayServiceTests()
    {
        service = new TrayService(sender, backend);
    }

    [Fact]
    public async Task ExposesStatusNotifierItemProperties()
    {
        var reply = await CallAsync(TrayService.ItemPath, "org.freedesktop.DBus.Properties", "GetAll", "s", TrayService.ItemInterface);
        Assert.Equal("a{sv}", reply.Signature);
        var properties = ((IEnumerable<object>)reply.Body[0]).Cast<DBusDictEntry>().ToDictionary(entry => (string)entry.Key, entry => ((DBusVariant)entry.Value).Value);
        Assert.Equal(true, properties["ItemIsMenu"]);
        Assert.Equal(new DBusObjectPath(TrayService.MenuPath), properties["Menu"]);
        Assert.Equal(TrayService.IconName, properties["IconName"]);
        Assert.Equal("Active", properties["Status"]);
        Assert.Equal(TrayIconPixmap.Sizes.Length, ((IEnumerable<object>)properties["IconPixmap"]).Count());

        var single = await CallAsync(TrayService.ItemPath, "org.freedesktop.DBus.Properties", "Get", "ss", TrayService.ItemInterface, "Title");
        Assert.Equal(AppText.AppName, ((DBusVariant)single.Body[0]).Value);
    }

    [Fact]
    public async Task ReportsUnknownPropertiesAndMethods()
    {
        var property = await CallAsync(TrayService.ItemPath, "org.freedesktop.DBus.Properties", "Get", "ss", TrayService.ItemInterface, "Nope");
        Assert.Equal(DBusErrors.UnknownProperty, property.ErrorName);
        var method = await CallAsync(TrayService.MenuPath, TrayService.MenuInterface, "Explode");
        Assert.Equal(DBusErrors.UnknownMethod, method.ErrorName);
        var path = await CallAsync("/elsewhere", "a.b", "C");
        Assert.Equal(DBusErrors.UnknownMethod, path.ErrorName);
        var set = await CallAsync(TrayService.ItemPath, "org.freedesktop.DBus.Properties", "Set", "ssv", TrayService.ItemInterface, "Title", new DBusVariant("s", "x"));
        Assert.Equal(DBusErrors.PropertyReadOnly, set.ErrorName);
    }

    [Fact]
    public async Task ServesTheMenuLayoutAfterARefresh()
    {
        await service.RefreshAsync();
        var signal = Assert.Single(sender.Sent);
        Assert.Equal(("LayoutUpdated", "ui"), (signal.Member!, signal.Signature));

        var reply = await CallAsync(TrayService.MenuPath, TrayService.MenuInterface, "GetLayout", "iias", 0, -1, new List<object>());
        Assert.Equal("u(ia{sv}av)", reply.Signature);
        Assert.Equal(service.Revision, reply.Body[0]);
        Assert.Equal("LG ULTRAGEAR", DBusMenuModelTests.Label(DBusMenuModelTests.Children((object[])reply.Body[1])[0]));
    }

    [Fact]
    public async Task ClickingAnInputActivatesItAndKeepsTheLayout()
    {
        await service.RefreshAsync();
        sender.Sent.Clear();
        var reply = await CallAsync(TrayService.MenuPath, TrayService.MenuInterface, "Event", "isvu", 5, "clicked", new DBusVariant("i", 0), 0u);
        Assert.Equal(DBusMessageType.MethodReturn, reply.Type);
        var input = Assert.IsType<TrayInput>(Assert.Single(backend.Activated));
        Assert.Equal("HDMI 1", input.Input.Name);
        Assert.DoesNotContain(sender.Sent, message => message.Member == "LayoutUpdated");
    }

    [Fact]
    public async Task IgnoresNonClickEventsAndReportsUnknownIds()
    {
        await service.RefreshAsync();
        await CallAsync(TrayService.MenuPath, TrayService.MenuInterface, "Event", "isvu", 5, "hovered", new DBusVariant("s", ""), 0u);
        Assert.Empty(backend.Activated);
        var reply = await CallAsync(
            TrayService.MenuPath,
            TrayService.MenuInterface,
            "EventGroup",
            "a(isvu)",
            new List<object> { new object[] { 2, "clicked", new DBusVariant("i", 0), 0u }, new object[] { 999, "clicked", new DBusVariant("i", 0), 0u } });
        Assert.Equal([999], (IEnumerable<object>)reply.Body[0]);
        Assert.Single(backend.Activated);
    }

    [Fact]
    public async Task AboutToShowRebuildsAndSignalsOnlyOnChange()
    {
        await service.RefreshAsync();
        sender.Sent.Clear();
        var unchanged = await CallAsync(TrayService.MenuPath, TrayService.MenuInterface, "AboutToShow", "i", 0);
        Assert.Equal(false, unchanged.Body[0]);
        Assert.DoesNotContain(sender.Sent, message => message.Member == "LayoutUpdated");

        backend.Menu = Displays.Menu(Displays.Lg, Displays.Dell);
        var changed = await CallAsync(TrayService.MenuPath, TrayService.MenuInterface, "AboutToShow", "i", 0);
        Assert.Equal(true, changed.Body[0]);
        Assert.Single(sender.Sent, message => message.Member == "LayoutUpdated");

        var group = await CallAsync(TrayService.MenuPath, TrayService.MenuInterface, "AboutToShowGroup", "ai", new List<object> { 0 });
        Assert.Equal("aiai", group.Signature);
        Assert.Empty((IEnumerable<object>)group.Body[0]);
    }

    [Fact]
    public async Task SendsItemsPropertiesUpdatedWhenOnlyTogglesChange()
    {
        await service.RefreshAsync();
        sender.Sent.Clear();
        backend.Menu = TrayMenu.Build([Displays.Lg], new SettingsLoad(new Settings(), null), new TrayCommandLabels("Start at Login", "Quit"), startsAtLogin: true);

        await service.RefreshAsync();

        var signal = Assert.Single(sender.Sent);
        Assert.Equal(("ItemsPropertiesUpdated", "a(ia{sv})a(ias)"), (signal.Member!, signal.Signature));
        var decoded = DBusMessage.Decode(signal.Encode(1));
        var updated = (object[])Assert.Single((IEnumerable<object>)decoded.Body[0]);
        Assert.Equal(12, updated[0]);
    }

    [Fact]
    public async Task IntrospectionIsValidXmlForEveryPath()
    {
        foreach (var path in new[] { "/", TrayService.ItemPath, TrayService.MenuPath })
        {
            var reply = await CallAsync(path, "org.freedesktop.DBus.Introspectable", "Introspect");
            var interfaces = XDocument.Parse((string)reply.Body[0]).Descendants("interface").Select(element => (string)element.Attribute("name")!);
            Assert.Contains("org.freedesktop.DBus.Properties", interfaces);
        }
        var menu = await CallAsync(TrayService.MenuPath, "org.freedesktop.DBus.Introspectable", "Introspect");
        Assert.Contains("com.canonical.dbusmenu", (string)menu.Body[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnswersPingAndHonorsNoReplyFlag()
    {
        var ping = await CallAsync("/", "org.freedesktop.DBus.Peer", "Ping");
        Assert.Equal(DBusMessageType.MethodReturn, ping.Type);
        sender.Sent.Clear();
        await service.HandleAsync(Call("/", "org.freedesktop.DBus.Peer", "Ping", "") with { Flags = DBusMessageFlags.NoReplyExpected });
        Assert.Empty(sender.Sent);
    }

    [Theory]
    [InlineData(":1.9", true)]
    [InlineData("", false)]
    public async Task ReportsWatcherOwnerChanges(string newOwner, bool expected)
    {
        bool? present = null;
        service.WatcherChanged += value => present = value;
        await service.HandleAsync(DBusMessage.Signal(DBusConnection.BusPath, DBusConnection.BusName, "NameOwnerChanged", "sss", TrayService.WatcherName, "", newOwner));
        Assert.Equal(expected, present);
    }

    [Fact]
    public async Task IgnoresOtherNameOwnerChanges()
    {
        bool? present = null;
        service.WatcherChanged += value => present = value;
        await service.HandleAsync(DBusMessage.Signal(DBusConnection.BusPath, DBusConnection.BusName, "NameOwnerChanged", "sss", "org.other", "", ":1.2"));
        Assert.Null(present);
    }

    private async Task<DBusMessage> CallAsync(string path, string @interface, string member, string signature = "", params object[] body)
    {
        sender.Sent.RemoveAll(message => message.Type != DBusMessageType.Signal);
        var call = Call(path, @interface, member, signature, body);
        await service.HandleAsync(DBusMessage.Decode(call.Encode(77)) with { Sender = ":1.3" });
        var reply = sender.Sent.Last(message => message.Type != DBusMessageType.Signal);
        Assert.Equal(77u, reply.ReplySerial);
        return DBusMessage.Decode(reply.Encode(1));
    }

    private static DBusMessage Call(string path, string @interface, string member, string signature, params object[] body) =>
        DBusMessage.MethodCall("org.kde.StatusNotifierItem-1-1", path, @interface, member, signature, body);
}
