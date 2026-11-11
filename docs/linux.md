# UltraGear Input on Linux

UltraGear Input puts a display icon in the top bar. Click it, pick an input, and the monitor
switches. It talks to the monitor over DDC/CI through the kernel I2C driver, and supports both
the LG command used by recent UltraGear models and the standard command used by most monitors.

## Install

1. Download `ultragear-input-<version>-linux-x64.tar.gz` (or `-arm64` for ARM computers) from
   [Releases](https://github.com/gabsdk/ultragear-input/releases) and its `.sha256`.
2. Check and unpack it:
   ```sh
   sha256sum -c ultragear-input-*-linux-x64.tar.gz.sha256
   tar -xzf ultragear-input-*-linux-x64.tar.gz
   cd ultragear-input-*-linux-x64
   ```
3. Run the installer as your normal user:
   ```sh
   ./install.sh --autostart
   ```

The installer:

- copies the program to `~/.local/bin/ultragear-input`;
- adds **UltraGear Input** to the app grid (`~/.local/share/applications/ultragear-input.desktop`);
- with `--autostart`, starts it when you log in (`~/.config/autostart/ultragear-input.desktop`);
- asks for `sudo` once to install `/etc/udev/rules.d/60-ultragear-input.rules` and
  `/etc/modules-load.d/ultragear-input.conf`, load the `i2c-dev` driver and apply the rule;
- starts the app.

Options: `--prefix DIR` installs to `DIR/bin`, `--no-system` skips the `sudo` step, `--no-start`
does not start the app. The program is a single self-contained file; it needs no .NET runtime
or other package.

## Desktop support

The icon uses the StatusNotifierItem standard, which most Linux desktops show in their top bar
or panel.

| Desktop | Status | What you need |
|---|---|---|
| GNOME on Ubuntu | Tested (GNOME 46, Ubuntu 24.04) | Nothing. Ubuntu ships the AppIndicator extension enabled. |
| GNOME on Fedora, Debian, Arch and others | Same extension as Ubuntu | Install `gnome-shell-extension-appindicator`, log out and in, then `gnome-extensions enable appindicatorsupport@rgcjonas.gmail.com` |
| Xfce | Tested (xfce4-panel 4.18) | The Status Tray plugin, present in the default panel |
| KDE Plasma | Expected to work, not tested | Nothing. Plasma implements the standard natively. |
| Cinnamon, Budgie, MATE, LXQt | Expected to work, not tested | Their tray or status applet |
| Sway, Hyprland and other Wayland compositors | Expected to work, not tested | A bar with a tray, such as Waybar with the `tray` module |

If no tray is running, the app keeps running, shows a notification after 15 seconds and adds its
icon as soon as a tray appears. Restarting GNOME Shell or the panel does not lose the icon.

## Use

The menu lists every external monitor with its inputs, the same as on macOS and Windows:

```
LG ULTRAGEAR
    DisplayPort 1
    DisplayPort 2 / USB-C
    USB-C / DisplayPort 3
    HDMI 1
    HDMI 2
    Protocol ▸  ● LG
                  Standard DDC/CI
──────────────
Open Settings File…
Start at Login
──────────────
Quit UltraGear Input
```

- LG monitors start on the **LG** protocol, others on **Standard DDC/CI**. If nothing happens,
  try the other one.
- Errors appear as desktop notifications, for example when the I2C device is missing or the
  permission is not granted.
- The laptop's built-in panel is not listed, because it has no DDC/CI.
- The menu follows hotplug: it rescans every 5 seconds and each time it opens.

The settings file is `~/.config/ultragear-input/settings.json` (or `$XDG_CONFIG_HOME`). Its format
is the same on every platform; see the main [README](https://github.com/gabsdk/ultragear-input#settings-file).

## Check what the app sees

```sh
ultragear-input --list-displays
```

```json
[
  {
    "key": "edid-1e6dbf5b010101010c22",
    "name": "LG ULTRAGEAR",
    "manufacturerCode": "GSM",
    "protocol": "lg"
  }
]
```

Other flags: `--version`, and `--install-launcher` to recreate the app grid entry.

## Permissions

Switching the input writes to `/dev/i2c-N`. By default only root can do that. The installed rule
gives the person logged in at the computer access to the I2C buses of display controllers only
(PCI class `0x0300` and `0x0380`), using the systemd `uaccess` tag, as
[ddcutil](https://www.ddcutil.com/i2c_permissions/) does:

```
SUBSYSTEM=="i2c-dev", KERNEL=="i2c-[0-9]*", ATTRS{class}=="0x030000", TAG+="uaccess"
SUBSYSTEM=="i2c-dev", KERNEL=="i2c-[0-9]*", ATTRS{class}=="0x038000", TAG+="uaccess"
```

Other I2C buses, such as the memory SPD or touchpad buses, stay closed.

Systems without systemd-logind (for example with eudev), and boards whose graphics are not a PCI
device (for example the Raspberry Pi), need the group method instead:

```sh
sudo groupadd --system i2c
sudo usermod -aG i2c "$USER"
echo 'KERNEL=="i2c-[0-9]*", GROUP="i2c", MODE="0660"' | sudo tee /etc/udev/rules.d/61-i2c-group.rules
sudo udevadm control --reload-rules && sudo udevadm trigger --action=add --subsystem-match=i2c-dev
```

Then log out and back in.

## Troubleshooting

| Message or symptom | Cause | Fix |
|---|---|---|
| `/dev/i2c-N does not exist` | The `i2c-dev` driver is not loaded | `sudo modprobe i2c-dev`, or run `install.sh` again |
| `No permission to use /dev/i2c-N` | The udev rule did not apply to your session | Run `install.sh` again, then log out and in. Check with `getfacl /dev/i2c-N`. |
| `No DDC/CI channel was found for this display` | The driver exposes no I2C bus for the port | Connect the monitor straight to the graphics card, without a dock or adapter |
| `The display did not accept the command (errno 121)` | The monitor did not answer on DDC/CI | Turn on DDC/CI in the monitor menu, or try the other protocol |
| No icon on GNOME | The AppIndicator extension is missing or disabled | See [Desktop support](#desktop-support) |
| The monitor is not in the menu | Its EDID is not readable, or it is the built-in panel | Run `ultragear-input --list-displays` and check `/sys/class/drm/*/status` |

With the proprietary NVIDIA driver, the connector has no direct link to its I2C bus. The app then
reads the EDID of each NVIDIA I2C bus and matches it to the connected monitor, so the first menu
open can take a little longer.

## Uninstall

```sh
./uninstall.sh            # program, launcher and Start at Login
./uninstall.sh --system   # also the udev rule and the i2c-dev setting (sudo)
./uninstall.sh --purge    # also the settings file
```

## How it works

| Part | How |
|---|---|
| Top bar icon | Registers `org.kde.StatusNotifierItem-<pid>-1` with `org.kde.StatusNotifierWatcher` on the session bus |
| Menu | Exports `com.canonical.dbusmenu` at `/MenuBar`; layout changes send `LayoutUpdated`, check and radio changes send `ItemsPropertiesUpdated` |
| Single instance | Owns `dev.gabsdk.UltraGearInput` on the session bus |
| Notifications | `org.freedesktop.Notifications.Notify` |
| Monitor list | Reads `/sys/class/drm/card*-*/{status,edid}` and the `ddc` link or `i2c-N` child of each connector |
| Input switch | Opens `/dev/i2c-N`, selects address `0x37` and writes the 7-byte DDC/CI frame; adapters without plain I2C use the SMBus I2C block write, which puts the same bytes on the wire |

The D-Bus client, the StatusNotifierItem and the menu are implemented in the app, so it does not
depend on libappindicator, GTK or Qt. See [protocol.md](protocol.md) for the frame bytes and
[architecture.md](architecture.md) for the code layout and the tests.

`ULTRAGEAR_INPUT_SYSFS` and `ULTRAGEAR_INPUT_DEV` replace `/sys` and `/dev`. The automated tests
use them to run against a fake sysfs tree.
