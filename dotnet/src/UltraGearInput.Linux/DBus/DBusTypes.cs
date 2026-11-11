namespace UltraGearInput.Linux.DBus;

internal readonly record struct DBusObjectPath(string Value);

internal readonly record struct DBusSignature(string Value);

internal sealed record DBusVariant(string Signature, object Value);

internal sealed record DBusDictEntry(object Key, object Value);

internal sealed class DBusException(string name, string message) : Exception(message)
{
    public string Name { get; } = name;
}

internal static class DBusErrors
{
    public const string Failed = "org.freedesktop.DBus.Error.Failed";
    public const string UnknownMethod = "org.freedesktop.DBus.Error.UnknownMethod";
    public const string UnknownProperty = "org.freedesktop.DBus.Error.UnknownProperty";
    public const string InvalidArgs = "org.freedesktop.DBus.Error.InvalidArgs";
    public const string PropertyReadOnly = "org.freedesktop.DBus.Error.PropertyReadOnly";
}

internal static class DBusValues
{
    public static IReadOnlyList<object> Dictionary(params (string Key, DBusVariant Value)[] entries) =>
        [.. entries.Select(entry => (object)new DBusDictEntry(entry.Key, entry.Value))];

    public static DBusVariant Of(string value) => new("s", value);

    public static DBusVariant Of(int value) => new("i", value);

    public static DBusVariant Of(uint value) => new("u", value);

    public static DBusVariant Of(bool value) => new("b", value);

    public static DBusVariant Of(DBusObjectPath value) => new("o", value);
}
