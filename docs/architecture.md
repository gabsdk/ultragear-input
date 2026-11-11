# Architecture

## Layout

```
spec/                     Shared test vectors (frames, inputs, settings, EDIDs)
macos/                    Swift package: menu bar app for macOS
dotnet/
  src/UltraGearInput.Core           Platform-free logic shared by Windows and Linux
  src/UltraGearInput.Windows        Windows Forms notification area app
  src/UltraGearInput.Windows.Native Win32, NVAPI and AMD ADL interop
  src/UltraGearInput.Linux          Linux top bar app
  tests/UltraGearInput.Tests        Unit tests for every .NET project
linux/
  packaging/              install.sh, uninstall.sh, udev rule, i2c-dev setting
  scripts/package.sh      Builds the Linux archives
  tests/                  Real GNOME Shell and Xfce test, installer test
docs/                     Protocol, Linux guide, this file
```

## Shared logic

`UltraGearInput.Core` holds everything that does not touch the operating system:

| Type | Role |
|---|---|
| `DdcFrame`, `DdcProtocol` | Builds the Set VCP Feature frame for the standard and LG protocols |
| `InputSource` | Default inputs and their standard and LG values |
| `Settings`, `SettingsStore` | Settings file format, validation and atomic save |
| `DisplayDescriptor`, `ManufacturerCode`, `Edid` | Monitor identity and LG detection |
| `TrayMenu` | The menu as a list of entries, rendered by each platform |
| `AppText` | Text shared by the Windows and Linux menus |

The Windows app renders `TrayMenu` entries as `ToolStripItem`s. The Linux app renders them as a
`com.canonical.dbusmenu` tree. Both menus therefore always have the same items in the same order.
The macOS app is written in Swift and keeps the same structure; the shared vectors in
[`spec/protocol.json`](../spec/protocol.json) keep both implementations in agreement.

## Linux app

| Folder | Content |
|---|---|
| `DBus/` | D-Bus wire format (`DBusWriter`, `DBusReader`, `DBusMessage`), address parsing, a socket connection with EXTERNAL authentication |
| `Tray/` | `TrayService` (StatusNotifierItem, dbusmenu, introspection), `DBusMenuModel`, icon pixmaps, notifications, `LinuxTrayBackend` |
| `Hardware/` | `SysfsScanner`, `DisplayResolver`, `DisplayInventory`, `PathResolver`, libc and I2C access |
| root | `Program` (flags), `TrayHost` (bus names, registration, refresh loop, signals), `LinuxEnvironment`, `DesktopEntry`, `LinuxText` |

Decisions:

- **No runtime dependencies.** The D-Bus protocol is implemented in the app instead of using a
  NuGet package or libappindicator, so the archive is one self-contained, trimmed file that runs on
  any glibc distribution.
- **Calls never wait inside the read loop.** Incoming method calls are handled in order on the
  read loop and only send replies or signals. Outgoing calls that wait for an answer
  (registration) run outside it.
- **Property changes do not rebuild the menu.** When only a check or radio state or a label
  changes, `DBusMenuModel` reports `ItemsPropertiesUpdated`; structure changes bump the revision
  and send `LayoutUpdated`. Hosts that queue updates while the menu is closed (GNOME) apply them on
  the next open.
- **Physical path resolution.** sysfs uses relative symbolic links that only make sense after the
  link's own directory is resolved. `PathResolver.RealPath` resolves every component before
  following `..`.
- **Least privilege.** The udev rule opens only display controller I2C buses, and the app only
  writes to address `0x37` (DDC/CI) and reads `0x50` (EDID).

## Tests

| Command | What it covers |
|---|---|
| `swift test` in `macos/` | Swift core against `spec/protocol.json` |
| `dotnet test --solution UltraGearInput.slnx` in `dotnet/` | .NET core, Windows interop layouts against the SDK headers, the D-Bus wire format against hand-built spec vectors, a socket conversation with a fake bus, dbusmenu and StatusNotifierItem behavior, sysfs discovery on temporary trees with real symlinks |
| `linux/tests/run-in-container.sh <binary>` | Real GNOME Shell 46 with the AppIndicator extension and a real Xfce panel, in a container, as a non-root user with an isolated home |
| `linux/tests/installer.sh <archive> [--system]` | Installs and removes the archive in a temporary home; `--system` also installs the udev rule, loads `i2c-dev` and checks the rule with `udevadm verify` |
| `bash linux/tests/kernel.test.sh` | Checks built-in I2C interface detection, conditional module installation and stub load failures without changing the host kernel |

Filesystem fixtures use host-native paths, portable controller names and directory symlinks, so
the full .NET suite can also run on Windows without skipping Linux discovery tests. The real
Linux desktop test still exercises Linux sysfs paths and PCI controller names.

The installer test checks `/sys/class/i2c-dev`, which exists when the interface is available
whether the driver is built in or loaded as a module. CI uses `modinfo` to check for `i2c-stub`
before requesting a matching `linux-modules-extra` package. Module loading and package failures
remain fatal; the integration test is never silently skipped.

The GNOME test starts the app before GNOME Shell, then checks that it registers when the shell
appears, that the icon is visible, that the shell renders the whole menu with headers, a submenu,
radio and check states, and it clicks items through the extension's own code path: switching
inputs, changing the protocol, toggling Start at Login and quitting. Errors are checked in the
shell's notification list.

In CI the same test runs on x64 and arm64 runners with the kernel `i2c-stub` driver providing a
real I2C bus with a chip at `0x37`. The test reads the chip's registers back and compares them with
the frames in `spec/protocol.json`, for both the LG and the standard protocol. `i2c-stub` only
speaks SMBus, so this exercises the SMBus I2C block write path; graphics card buses take the plain
`write()` path, which sends the same bytes.

Not covered automatically: a real monitor switching input, and the NVIDIA EDID matching on a real
NVIDIA card.
