namespace UltraGearInput.Core;

public abstract record TrayMenuEntry;

public sealed record TraySeparator : TrayMenuEntry;

public sealed record TrayNotice(string Text) : TrayMenuEntry;

public sealed record TrayHeader(string Text) : TrayMenuEntry;

public sealed record TrayInput(DisplayDescriptor Display, InputSource Input, DdcProtocol Protocol) : TrayMenuEntry
{
    public ushort Value => Input.ValueFor(Protocol);
}

public sealed record TrayProtocolChoice(DisplayDescriptor Display, DdcProtocol Protocol, bool IsSelected) : TrayMenuEntry
{
    public string Text => AppText.ProtocolName(Protocol);
}

public sealed record TrayProtocolMenu(IReadOnlyList<TrayProtocolChoice> Choices) : TrayMenuEntry
{
    public string Text { get; } = AppText.ProtocolMenu;
}

public enum TrayCommandKind
{
    OpenSettings,
    StartAtLogin,
    Quit,
}

public sealed record TrayCommand(TrayCommandKind Kind, string Text, bool? IsChecked = null) : TrayMenuEntry;

public sealed record TrayCommandLabels(string StartAtLogin, string Quit);

public static class TrayMenu
{
    public static IReadOnlyList<TrayMenuEntry> Build(
        IReadOnlyList<DisplayDescriptor> displays,
        SettingsLoad loaded,
        TrayCommandLabels labels,
        bool startsAtLogin)
    {
        var entries = new List<TrayMenuEntry>();
        if (displays.Count == 0)
        {
            entries.Add(new TrayNotice(AppText.NoDisplays));
        }
        foreach (var display in displays)
        {
            if (entries.Count > 0)
            {
                entries.Add(new TraySeparator());
            }
            entries.AddRange(DisplaySection(display, loaded.Settings));
        }
        entries.Add(new TraySeparator());
        if (loaded.Error is { } error)
        {
            entries.Add(new TrayNotice(AppText.InvalidSettings(error)));
        }
        entries.Add(new TrayCommand(TrayCommandKind.OpenSettings, AppText.OpenSettings));
        entries.Add(new TrayCommand(TrayCommandKind.StartAtLogin, labels.StartAtLogin, startsAtLogin));
        entries.Add(new TraySeparator());
        entries.Add(new TrayCommand(TrayCommandKind.Quit, labels.Quit));
        return entries;
    }

    private static IEnumerable<TrayMenuEntry> DisplaySection(DisplayDescriptor display, Settings settings)
    {
        var selected = settings.ProtocolFor(display);
        yield return new TrayHeader(display.Name);
        foreach (var input in settings.ResolvedInputs)
        {
            yield return new TrayInput(display, input, selected);
        }
        yield return new TrayProtocolMenu(
            [.. Enum.GetValues<DdcProtocol>().Select(option => new TrayProtocolChoice(display, option, option == selected))]);
    }
}
