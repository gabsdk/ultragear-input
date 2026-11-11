using System.Text;
using UltraGearInput.Core;
using UltraGearInput.Linux.DBus;

namespace UltraGearInput.Linux.Tray;

internal enum MenuChangeKind
{
    None,
    Layout,
    Properties,
}

internal sealed record MenuChange(MenuChangeKind Kind, IReadOnlyList<object> Updated, IReadOnlyList<object> Removed)
{
    public static MenuChange None { get; } = new(MenuChangeKind.None, [], []);

    public static MenuChange Layout { get; } = new(MenuChangeKind.Layout, [], []);
}

internal sealed class DBusMenuModel
{
    public const int RootId = 0;
    public const string NodeSignature = "(ia{sv}av)";

    private sealed record Node(int Id, IReadOnlyDictionary<string, DBusVariant> Properties, IReadOnlyList<int> Children, TrayMenuEntry? Action);

    private Dictionary<int, Node> nodes = new() { [RootId] = Root([]) };
    private string structure = "";
    private bool hasLayout;

    public uint Revision { get; private set; } = 1;

    public MenuChange Update(IReadOnlyList<TrayMenuEntry> entries)
    {
        var next = new Dictionary<int, Node>();
        var children = new List<int>();
        var nextId = RootId + 1;
        foreach (var entry in entries)
        {
            children.Add(Add(next, entry, ref nextId));
        }
        next[RootId] = Root(children);

        var previous = nodes;
        var nextStructure = Structure(next);
        nodes = next;
        if (!hasLayout || nextStructure != structure)
        {
            hasLayout = true;
            structure = nextStructure;
            Revision++;
            return MenuChange.Layout;
        }
        var updated = new List<object>();
        var removed = new List<object>();
        foreach (var node in next.Values.OrderBy(node => node.Id))
        {
            var before = previous[node.Id].Properties;
            if (!SameProperties(before, node.Properties))
            {
                updated.Add(new object[] { node.Id, Properties(node, []) });
                var missing = before.Keys.Where(key => !node.Properties.ContainsKey(key)).Order(StringComparer.Ordinal).Select(key => (object)key).ToList();
                if (missing.Count > 0)
                {
                    removed.Add(new object[] { node.Id, missing });
                }
            }
        }
        return updated.Count == 0 ? MenuChange.None : new MenuChange(MenuChangeKind.Properties, updated, removed);
    }

    public bool Contains(int id) => nodes.ContainsKey(id);

    public TrayMenuEntry? ActionFor(int id) => nodes.TryGetValue(id, out var node) ? node.Action : null;

    public object[] Layout(int parentId, int depth, IReadOnlyList<string> propertyNames)
    {
        if (!nodes.TryGetValue(parentId, out var node))
        {
            throw new DBusException(DBusErrors.InvalidArgs, $"Unknown menu item {parentId}.");
        }
        return LayoutOf(node, depth, propertyNames);
    }

    public IReadOnlyList<object> GroupProperties(IReadOnlyList<int> ids, IReadOnlyList<string> propertyNames)
    {
        var selected = ids.Count == 0 ? nodes.Keys.Order() : ids.Where(nodes.ContainsKey);
        return [.. selected.Select(id => (object)new object[] { id, Properties(nodes[id], propertyNames) })];
    }

    public DBusVariant Property(int id, string name) =>
        nodes.TryGetValue(id, out var node) && node.Properties.TryGetValue(name, out var value)
            ? value
            : throw new DBusException(DBusErrors.InvalidArgs, $"Menu item {id} has no property {name}.");

    public static string EscapeLabel(string text) => text.Replace("_", "__", StringComparison.Ordinal);

    private object[] LayoutOf(Node node, int depth, IReadOnlyList<string> propertyNames)
    {
        List<object> children = depth == 0
            ? []
            : [.. node.Children.Select(id => (object)new DBusVariant(NodeSignature, LayoutOf(nodes[id], depth - 1, propertyNames)))];
        return [node.Id, Properties(node, propertyNames), children];
    }

    private static IReadOnlyList<object> Properties(Node node, IReadOnlyList<string> propertyNames) =>
        [.. node.Properties
            .Where(property => propertyNames.Count == 0 || propertyNames.Contains(property.Key))
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => (object)new DBusDictEntry(property.Key, property.Value))];

    private static int Add(Dictionary<int, Node> target, TrayMenuEntry entry, ref int nextId)
    {
        var id = nextId++;
        var children = new List<int>();
        if (entry is TrayProtocolMenu protocolMenu)
        {
            foreach (var choice in protocolMenu.Choices)
            {
                children.Add(Add(target, choice, ref nextId));
            }
        }
        target[id] = new Node(id, PropertiesOf(entry), children, IsActionable(entry) ? entry : null);
        return id;
    }

    private static bool IsActionable(TrayMenuEntry entry) => entry is TrayInput or TrayProtocolChoice or TrayCommand;

    private static Dictionary<string, DBusVariant> PropertiesOf(TrayMenuEntry entry) => entry switch
    {
        TraySeparator => new() { ["type"] = DBusValues.Of("separator") },
        TrayNotice notice => Disabled(notice.Text),
        TrayHeader header => Disabled(header.Text),
        TrayInput input => Label(input.Input.Name),
        TrayProtocolMenu menu => new(Label(menu.Text)) { ["children-display"] = DBusValues.Of("submenu") },
        TrayProtocolChoice choice => Toggle(choice.Text, "radio", choice.IsSelected),
        TrayCommand { IsChecked: { } isChecked } command => Toggle(command.Text, "checkmark", isChecked),
        TrayCommand command => Label(command.Text),
        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
    };

    private static Dictionary<string, DBusVariant> Label(string text) => new() { ["label"] = DBusValues.Of(EscapeLabel(text)) };

    private static Dictionary<string, DBusVariant> Disabled(string text) =>
        new(Label(text)) { ["enabled"] = DBusValues.Of(false) };

    private static Dictionary<string, DBusVariant> Toggle(string text, string toggleType, bool isOn) =>
        new(Label(text))
        {
            ["toggle-type"] = DBusValues.Of(toggleType),
            ["toggle-state"] = DBusValues.Of(isOn ? 1 : 0),
        };

    private static Node Root(IReadOnlyList<int> children) =>
        new(RootId, new Dictionary<string, DBusVariant> { ["children-display"] = DBusValues.Of("submenu") }, children, null);

    private static readonly string[] StructuralProperties = ["type", "children-display", "toggle-type"];

    private static string Structure(Dictionary<int, Node> target)
    {
        var builder = new StringBuilder();
        foreach (var node in target.Values.OrderBy(node => node.Id))
        {
            builder.Append(node.Id).Append('[').AppendJoin(',', node.Children).Append(']');
            foreach (var key in StructuralProperties)
            {
                builder.Append(key).Append('=').Append(node.Properties.GetValueOrDefault(key)?.Value).Append(';');
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static bool SameProperties(IReadOnlyDictionary<string, DBusVariant> left, IReadOnlyDictionary<string, DBusVariant> right) =>
        left.Count == right.Count
        && left.All(property => right.TryGetValue(property.Key, out var other) && Equals(property.Value.Value, other.Value));
}
