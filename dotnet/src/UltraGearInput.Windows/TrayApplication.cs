using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using UltraGearInput.Core;
using UltraGearInput.Core.Updates;

namespace UltraGearInput.Windows;

[SupportedOSPlatform("windows")]
internal sealed class TrayApplication : ApplicationContext
{
    private const int BalloonTimeout = 5000;

    private readonly SettingsStore store;
    private readonly HttpClient updateHttp = UpdateClient.CreateHttpClient();
    private readonly UpdateSession updates;
    private readonly TrayIconRenderer iconRenderer = new();
    private readonly ContextMenuStrip menu = new();
    private readonly Font headerFont;
    private readonly NotifyIcon notifyIcon;
    private readonly SynchronizationContext uiContext;

    public TrayApplication(SettingsStore store)
    {
        this.store = store;
        uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var updateClient = new UpdateClient(updateHttp);
        updates = new UpdateSession(
            token => updateClient.CheckAsync(UpdateTarget.WindowsX64, UpdateInstaller.CurrentVersion, token),
            (release, token) => UpdateInstaller.PrepareAsync(updateClient, release, Environment.ProcessPath!, token),
            message => uiContext.Post(_ =>
            {
                if (!menu.IsDisposed)
                {
                    notifyIcon!.ShowBalloonTip(BalloonTimeout, AppText.AppName, message, ToolTipIcon.Info);
                }
            }, null),
            () => uiContext.Post(_ => ExitThread(), null),
            UpdateInstaller.CurrentVersion);
        updates.Changed += () => uiContext.Post(_ =>
        {
            if (!menu.IsDisposed)
            {
                Rebuild();
            }
        }, null);
        headerFont = new Font(menu.Font, FontStyle.Bold);
        menu.Opening += (_, _) => Rebuild();
        notifyIcon = new NotifyIcon
        {
            Icon = iconRenderer.Render(),
            Text = AppText.AppName,
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
            updates.Dispose();
            updateHttp.Dispose();
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
        var displays = InputSwitcher.Displays().Select(display => display.Descriptor).ToList();
        var entries = TrayMenu.Build(displays, store.Load(), Text.Labels, StartupRegistration.IsEnabled, updates.Label, updates.IsBusy);
        foreach (var entry in entries)
        {
            menu.Items.Add(Render(entry));
        }
    }

    private ToolStripItem Render(TrayMenuEntry entry) => entry switch
    {
        TraySeparator => new ToolStripSeparator(),
        TrayNotice notice => new ToolStripMenuItem(notice.Text) { Enabled = false },
        TrayHeader header => new ToolStripLabel(header.Text) { Font = headerFont },
        TrayInput input => new ToolStripMenuItem(input.Input.Name, null, (_, _) => SwitchInput(input)),
        TrayProtocolMenu protocolMenu => RenderProtocolMenu(protocolMenu),
        TrayCommand command => new ToolStripMenuItem(command.Text, null, (_, _) => Run(command.Kind))
        {
            Checked = command.IsChecked == true,
            Enabled = command.IsEnabled,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
    };

    private ToolStripMenuItem RenderProtocolMenu(TrayProtocolMenu protocolMenu)
    {
        var item = new ToolStripMenuItem(protocolMenu.Text);
        foreach (var choice in protocolMenu.Choices)
        {
            item.DropDownItems.Add(new ToolStripMenuItem(choice.Text, null, (_, _) => SelectProtocol(choice))
            {
                Checked = choice.IsSelected,
            });
        }
        return item;
    }

    private async void SwitchInput(TrayInput input)
    {
        try
        {
            await InputSwitcher.SwitchAsync(input.Display.Key, input.Value, input.Protocol).ConfigureAwait(false);
        }
        catch (SwitchException exception)
        {
            uiContext.Post(_ => ShowError(AppText.SwitchFailed, Text.Describe(exception)), null);
        }
    }

    private void SelectProtocol(TrayProtocolChoice choice)
    {
        try
        {
            store.SaveProtocol(choice.Protocol, choice.Display);
        }
        catch (SettingsException exception)
        {
            ShowError(AppText.SettingsFailed, AppText.InvalidSettings(exception.Error));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError(AppText.SettingsFailed, exception.Message);
        }
    }

    private void Run(TrayCommandKind kind)
    {
        switch (kind)
        {
            case TrayCommandKind.OpenSettings:
                OpenSettings();
                break;
            case TrayCommandKind.StartAtLogin:
                ToggleStartup();
                break;
            case TrayCommandKind.Update:
                _ = updates.ExecuteAsync();
                break;
            case TrayCommandKind.Quit:
                ExitThread();
                break;
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
            ShowError(AppText.SettingsFailed, exception.Message);
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
}
