using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using UltraGearInput.Core;

namespace UltraGearInput;

[SupportedOSPlatform("windows")]
internal sealed class TrayApplication : ApplicationContext
{
    private const int BalloonTimeout = 5000;

    private readonly SettingsStore store;
    private readonly TrayIconRenderer iconRenderer = new();
    private readonly ContextMenuStrip menu = new();
    private readonly Font headerFont;
    private readonly NotifyIcon notifyIcon;
    private readonly SynchronizationContext uiContext;

    public TrayApplication(SettingsStore store)
    {
        this.store = store;
        uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        headerFont = new Font(menu.Font, FontStyle.Bold);
        menu.Opening += (_, _) => Rebuild();
        notifyIcon = new NotifyIcon
        {
            Icon = iconRenderer.Render(),
            Text = Text.AppName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        notifyIcon.MouseUp += OnNotifyIconMouseUp;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Rebuild();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            menu.Dispose();
            headerFont.Dispose();
            iconRenderer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Rebuild()
    {
        menu.Items.Clear();
        var loaded = store.Load();
        var displays = InputSwitcher.Displays();

        if (displays.Count == 0)
        {
            menu.Items.Add(DisabledItem(Text.NoDisplays));
        }
        foreach (var display in displays)
        {
            AddSection(display.Descriptor, loaded.Settings);
        }

        menu.Items.Add(new ToolStripSeparator());
        if (loaded.Error is { } error)
        {
            menu.Items.Add(DisabledItem(Text.InvalidSettings(error)));
        }
        menu.Items.Add(new ToolStripMenuItem(Text.OpenSettings, null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripMenuItem(Text.StartWithWindows, null, (_, _) => ToggleStartup())
        {
            Checked = StartupRegistration.IsEnabled,
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Text.Exit, null, (_, _) => ExitThread()));
    }

    private void AddSection(DisplayDescriptor display, Settings settings)
    {
        var ddcProtocol = settings.ProtocolFor(display);
        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(new ToolStripLabel(display.Name) { Font = headerFont });
        foreach (var input in settings.ResolvedInputs)
        {
            var value = input.ValueFor(ddcProtocol);
            menu.Items.Add(new ToolStripMenuItem(input.Name, null, (_, _) => SwitchInput(display.Key, value, ddcProtocol)));
        }
        var protocolMenu = new ToolStripMenuItem(Text.ProtocolMenu);
        foreach (var option in Enum.GetValues<DdcProtocol>())
        {
            protocolMenu.DropDownItems.Add(new ToolStripMenuItem(Text.ProtocolName(option), null, (_, _) => SelectProtocol(display, option))
            {
                Checked = option == ddcProtocol,
            });
        }
        menu.Items.Add(protocolMenu);
    }

    private async void SwitchInput(string displayKey, ushort value, DdcProtocol ddcProtocol)
    {
        try
        {
            await InputSwitcher.SwitchAsync(displayKey, value, ddcProtocol).ConfigureAwait(false);
        }
        catch (SwitchException exception)
        {
            uiContext.Post(_ => ShowError(Text.SwitchFailed, Text.Describe(exception)), null);
        }
    }

    private void SelectProtocol(DisplayDescriptor display, DdcProtocol ddcProtocol)
    {
        var loaded = store.Load();
        if (loaded.Error is { } error)
        {
            ShowError(Text.SettingsFailed, Text.InvalidSettings(error));
            return;
        }
        try
        {
            store.Save(loaded.Settings.WithProtocol(ddcProtocol, display));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError(Text.SettingsFailed, exception.Message);
        }
    }

    private void OpenSettings()
    {
        try
        {
            store.EnsureFileExists();
            Process.Start(new ProcessStartInfo(store.FilePath) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            ShowError(Text.SettingsFailed, exception.Message);
        }
    }

    private void ToggleStartup()
    {
        try
        {
            StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowError(Text.StartupFailed, exception.Message);
        }
    }

    private void ShowError(string title, string message) =>
        notifyIcon.ShowBalloonTip(BalloonTimeout, title, message, ToolTipIcon.Warning);

    private void OnNotifyIconMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            menu.Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs eventArgs)
    {
        if (eventArgs.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            uiContext.Post(_ => notifyIcon.Icon = iconRenderer.Render(), null);
        }
    }

    private static ToolStripMenuItem DisabledItem(string text) => new(text) { Enabled = false };
}
