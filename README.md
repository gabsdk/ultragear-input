# UltraGear Input

Switch the input of an LG UltraGear monitor from the macOS menu bar or the Windows notification
area. No monitor joystick needed.

The app talks to the monitor over DDC/CI and supports the LG-specific command that recent LG
monitors require, plus the standard command used by most other monitors.

## Download

Get the latest build from [Releases](https://github.com/gabsdk/ultragear-input/releases).

| Platform | File | Requirements |
|---|---|---|
| macOS | `UltraGearInput-macos-<version>.zip` | macOS 14 or later, Apple silicon |
| Windows | `UltraGearInput-windows-x64-<version>.exe` | Windows 10 or 11, x64 |

Each file has a `.sha256` next to it.

### macOS

1. Unzip and move **UltraGear Input.app** to `/Applications`.
2. The app is not notarized. Clear the download flag once:
   ```sh
   xattr -dr com.apple.quarantine "/Applications/UltraGear Input.app"
   ```
3. Open the app. A display icon appears in the menu bar.

### Windows

1. Save the `.exe` anywhere, for example `%LOCALAPPDATA%\Programs\UltraGear Input`.
2. Run it. If SmartScreen warns about an unknown publisher, choose **More info** > **Run anyway**.
3. A display icon appears in the notification area next to the clock. Left or right click it.

## Use

The menu lists every external monitor with its inputs:

```
LG ULTRAGEAR
    DisplayPort 1
    DisplayPort 2 / USB-C
    USB-C / DisplayPort 3
    HDMI 1
    HDMI 2
    Protocol ▸  ✓ LG
                  Standard DDC/CI
──────────────
Open Settings File…
Launch at Login            (Start with Windows)
──────────────
Quit UltraGear Input       (Exit)
```

- Pick an input to switch the monitor.
- **Protocol** chooses how the command is sent. LG monitors start on **LG**; others start on
  **Standard DDC/CI**. If nothing happens, try the other one.
- **Launch at Login** / **Start with Windows** starts the app with your session.

Install the app on every computer connected to the monitor. Once the monitor shows another
computer, the first one may stop seeing it, so you switch back from the computer on screen.

## Settings file

**Open Settings File…** creates and opens the file:

- macOS: `~/Library/Application Support/UltraGear Input/settings.json`
- Windows: `%APPDATA%\UltraGear Input\settings.json`

```json
{
  "inputs": [
    { "id": "displayPort1", "name": "DisplayPort", "standardValue": 15, "lgValue": 208 },
    { "id": "hdmi1", "name": "PlayStation", "standardValue": 17, "lgValue": 144 }
  ],
  "protocols": {
    "<display key>": "standard"
  }
}
```

- `inputs` replaces the default list. Rename, reorder or remove entries to match your ports. Up to
  32 entries; `id` must be unique.
- `protocols` stores the protocol you chose per monitor. The menu writes it for you.

The menu reads the file every time it opens. If the file is invalid, the menu says so and uses the
defaults.

To see the display keys and the detected protocol:

```sh
"/Applications/UltraGear Input.app/Contents/MacOS/UltraGearInput" --list-displays
```

```powershell
.\UltraGearInput.exe --list-displays
```

## Compatibility

| Setup | Standard DDC/CI | LG protocol |
|---|---|---|
| Mac with Apple silicon, USB-C / Thunderbolt / DisplayPort | Yes | Yes |
| Mac with Apple silicon, built-in HDMI port | Depends on the Mac | Depends on the Mac |
| Intel Mac | No | No |
| Windows with NVIDIA graphics | Yes | Yes |
| Windows with AMD graphics | Yes | Yes |
| Windows with Intel graphics | Yes | No |

If the monitor menu has a DDC/CI option, turn it on. Docks, KVM switches and some adapters block
DDC/CI.

See [docs/protocol.md](docs/protocol.md) for the bytes sent and the input values.

## Build from source

### macOS

Requires Xcode 16 or later.

```sh
cd macos
swift test
./scripts/bundle.sh
open "dist/UltraGear Input.app"
```

### Windows

Requires the .NET 10 SDK. The tests also run on macOS and Linux.

```powershell
cd windows
dotnet test --solution UltraGearInput.slnx
dotnet publish src/UltraGearInput/UltraGearInput.csproj --configuration Release --output publish
.\publish\UltraGearInput.exe
```

### Checks

| Platform | Lint | Build | Test |
|---|---|---|---|
| macOS | `swift format lint --strict --recursive --configuration .swift-format Sources Tests Package.swift` | `swift build -Xswiftc -warnings-as-errors` | `swift test` |
| Windows | `dotnet format UltraGearInput.slnx --verify-no-changes` | `dotnet build UltraGearInput.slnx` | `dotnet test --solution UltraGearInput.slnx` |

Both test suites check their output against the same vectors in
[`spec/protocol.json`](spec/protocol.json).

## Branches and releases

- `main`: stable line and default branch. Changes arrive by pull request from `development`.
- `development`: daily work. `feature/*` and `fix/*` branch from it and merge back.
- CI runs lint, build, tests and a smoke test on both platforms for pushes and pull requests to
  `main` and `development`, and keeps the builds as artifacts for 14 days.
- A release is a `vX.Y.Z` tag on `main` that matches `VERSION`. The release workflow builds both
  apps and attaches them with their checksums.

## Acknowledgements

The LG command and input values were documented by the
[m1ddc](https://github.com/waydabber/m1ddc), [BetterDisplay](https://github.com/waydabber/BetterDisplay)
and [MonitorControl](https://github.com/MonitorControl/MonitorControl) communities. See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License

[MIT](LICENSE)
