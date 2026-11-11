using UltraGearInput.Core;
using UltraGearInput.Linux.DBus;

namespace UltraGearInput.Linux.Tray;

internal interface ITrayBackend
{
    IReadOnlyList<TrayMenuEntry> BuildMenu();

    void Activate(TrayMenuEntry entry);
}

internal sealed class TrayService(IDBusSender bus, ITrayBackend backend) : IDBusHandler
{
    public const string ItemPath = "/StatusNotifierItem";
    public const string MenuPath = "/MenuBar";
    public const string ItemInterface = "org.kde.StatusNotifierItem";
    public const string MenuInterface = "com.canonical.dbusmenu";
    public const string WatcherName = "org.kde.StatusNotifierWatcher";
    public const string WatcherPath = "/StatusNotifierWatcher";
    public const string IconName = "video-display-symbolic";

    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string IntrospectableInterface = "org.freedesktop.DBus.Introspectable";
    private const string PeerInterface = "org.freedesktop.DBus.Peer";

    private readonly Lock gate = new();
    private readonly DBusMenuModel menu = new();

    public event Action<bool>? WatcherChanged;

    public uint Revision
    {
        get
        {
            lock (gate)
            {
                return menu.Revision;
            }
        }
    }

    public async Task RefreshAsync() => await PublishAsync(Rebuild()).ConfigureAwait(false);

    public Task HandleAsync(DBusMessage message) => message.Type switch
    {
        DBusMessageType.MethodCall => HandleCallAsync(message),
        DBusMessageType.Signal => HandleSignal(message),
        _ => Task.CompletedTask,
    };

    private Task HandleSignal(DBusMessage message)
    {
        if (message is { Interface: DBusConnection.BusName, Member: "NameOwnerChanged" }
            && message.Body is [string name, string, string newOwner]
            && name == WatcherName)
        {
            WatcherChanged?.Invoke(newOwner.Length > 0);
        }
        return Task.CompletedTask;
    }

    private async Task HandleCallAsync(DBusMessage call)
    {
        var reply = call switch
        {
            { Interface: PeerInterface, Member: "Ping" } => call.Reply(),
            { Interface: IntrospectableInterface or null, Member: "Introspect" } => call.Reply("s", Introspection.For(call.Path ?? "/")),
            { Path: ItemPath } => HandleItem(call),
            { Path: MenuPath } => await HandleMenuAsync(call).ConfigureAwait(false),
            _ => UnknownMethod(call),
        };
        if (call.ExpectsReply)
        {
            await bus.SendAsync(reply).ConfigureAwait(false);
        }
    }

    private static DBusMessage HandleItem(DBusMessage call) => call switch
    {
        { Interface: PropertiesInterface, Member: "GetAll" } => call.Reply("a{sv}", ItemProperties()),
        { Interface: PropertiesInterface, Member: "Get", Body: [string, string name] } => Property(call, ItemProperties(), name),
        { Interface: PropertiesInterface, Member: "Set" } => call.ErrorReply(DBusErrors.PropertyReadOnly, "Properties are read-only."),
        { Member: "Activate" or "SecondaryActivate" or "ContextMenu" or "Scroll" or "ProvideXdgActivationToken" } => call.Reply(),
        _ => UnknownMethod(call),
    };

    private async Task<DBusMessage> HandleMenuAsync(DBusMessage call)
    {
        switch (call)
        {
            case { Interface: PropertiesInterface, Member: "GetAll" }:
                return call.Reply("a{sv}", MenuProperties());
            case { Interface: PropertiesInterface, Member: "Get", Body: [string, string name] }:
                return Property(call, MenuProperties(), name);
            case { Member: "GetLayout", Body: [int parentId, int depth, IReadOnlyList<object> names] }:
                lock (gate)
                {
                    return call.Reply("u" + DBusMenuModel.NodeSignature, menu.Revision, menu.Layout(parentId, depth, Strings(names)));
                }
            case { Member: "GetGroupProperties", Body: [IReadOnlyList<object> ids, IReadOnlyList<object> names] }:
                lock (gate)
                {
                    return call.Reply("a(ia{sv})", menu.GroupProperties([.. ids.Cast<int>()], Strings(names)));
                }
            case { Member: "GetProperty", Body: [int id, string name] }:
                lock (gate)
                {
                    return call.Reply("v", menu.Property(id, name));
                }
            case { Member: "Event", Body: [int id, string eventId, DBusVariant, uint] }:
                await OnEventAsync(id, eventId).ConfigureAwait(false);
                return call.Reply();
            case { Member: "EventGroup", Body: [IReadOnlyList<object> events] }:
                var missing = new List<object>();
                foreach (var item in events.Cast<object[]>())
                {
                    if (!await OnEventAsync((int)item[0], (string)item[1]).ConfigureAwait(false))
                    {
                        missing.Add(item[0]);
                    }
                }
                return call.Reply("ai", missing);
            case { Member: "AboutToShow", Body: [int] }:
                return call.Reply("b", await RefreshForDisplayAsync().ConfigureAwait(false));
            case { Member: "AboutToShowGroup", Body: [IReadOnlyList<object>] }:
                List<object> updates = await RefreshForDisplayAsync().ConfigureAwait(false) ? [DBusMenuModel.RootId] : [];
                return call.Reply("aiai", updates, new List<object>());
            default:
                return UnknownMethod(call);
        }
    }

    private async Task<bool> OnEventAsync(int id, string eventId)
    {
        TrayMenuEntry? action;
        lock (gate)
        {
            if (!menu.Contains(id))
            {
                return false;
            }
            action = menu.ActionFor(id);
        }
        if (eventId == "clicked" && action is not null)
        {
            backend.Activate(action);
            await RefreshAsync().ConfigureAwait(false);
        }
        return true;
    }

    private async Task<bool> RefreshForDisplayAsync()
    {
        var change = Rebuild();
        await PublishAsync(change).ConfigureAwait(false);
        return change.Kind != MenuChangeKind.None;
    }

    private MenuChange Rebuild()
    {
        var entries = backend.BuildMenu();
        lock (gate)
        {
            return menu.Update(entries);
        }
    }

    private Task PublishAsync(MenuChange change) => change.Kind switch
    {
        MenuChangeKind.Layout => bus.SendAsync(DBusMessage.Signal(MenuPath, MenuInterface, "LayoutUpdated", "ui", Revision, DBusMenuModel.RootId)),
        MenuChangeKind.Properties => bus.SendAsync(DBusMessage.Signal(MenuPath, MenuInterface, "ItemsPropertiesUpdated", "a(ia{sv})a(ias)", change.Updated, change.Removed)),
        _ => Task.CompletedTask,
    };

    private static List<string> Strings(IReadOnlyList<object> values) => [.. values.Cast<string>()];

    private static DBusMessage Property(DBusMessage call, IReadOnlyList<object> properties, string name) =>
        properties.Cast<DBusDictEntry>().FirstOrDefault(entry => (string)entry.Key == name) is { } entry
            ? call.Reply("v", entry.Value)
            : call.ErrorReply(DBusErrors.UnknownProperty, $"Unknown property {name}.");

    private static DBusMessage UnknownMethod(DBusMessage call) =>
        call.ErrorReply(DBusErrors.UnknownMethod, $"No method {call.Interface}.{call.Member} on {call.Path}.");

    public static IReadOnlyList<object> ItemProperties() => DBusValues.Dictionary(
        ("Category", DBusValues.Of("Hardware")),
        ("Id", DBusValues.Of("ultragear-input")),
        ("Title", DBusValues.Of(AppText.AppName)),
        ("Status", DBusValues.Of("Active")),
        ("WindowId", DBusValues.Of(0)),
        ("IconName", DBusValues.Of(IconName)),
        ("IconPixmap", new DBusVariant("a(iiay)", TrayIconPixmap.All())),
        ("IconThemePath", DBusValues.Of("")),
        ("OverlayIconName", DBusValues.Of("")),
        ("OverlayIconPixmap", new DBusVariant("a(iiay)", new List<object>())),
        ("AttentionIconName", DBusValues.Of("")),
        ("AttentionIconPixmap", new DBusVariant("a(iiay)", new List<object>())),
        ("AttentionMovieName", DBusValues.Of("")),
        ("ToolTip", new DBusVariant("(sa(iiay)ss)", new object[] { IconName, new List<object>(), AppText.AppName, "" })),
        ("ItemIsMenu", DBusValues.Of(true)),
        ("Menu", DBusValues.Of(new DBusObjectPath(MenuPath))));

    public static IReadOnlyList<object> MenuProperties() => DBusValues.Dictionary(
        ("Version", DBusValues.Of(3u)),
        ("TextDirection", DBusValues.Of("ltr")),
        ("Status", DBusValues.Of("normal")),
        ("IconThemePath", new DBusVariant("as", new List<object>())));
}
