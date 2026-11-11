# AGENTS.md — UltraGear Input

## Scope and working agreement

These instructions apply to the entire repository and to every coding agent working in it.
Follow explicit user instructions and applicable parent instructions; use this file for the
project-specific rules. Keep this document, the README and the implementation consistent.

UltraGear Input is a public, MIT-licensed personal project maintained at
`gabsdk/ultragear-input`. It switches external monitor inputs through DDC/CI using native desktop
menus. It is independent of Arara Code. Never copy private workspace source, internal prompts,
credentials, service URLs or proprietary assets into this repository.

- Complete the authorized task, including verification and documentation. Ask only when missing
  facts, a new dependency, a hardware operation or an irreversible action requires a decision.
- Inspect the current branch, working tree, relevant implementation and tests before editing.
  Preserve existing staged and unstaged work. A transferred conversation is context, not proof
  that a command ran successfully or authorization to publish.
- Keep changes focused. Do not replace working code, introduce a framework, reorganize unrelated
  files or change another platform merely to simplify the current task.
- Use English for code, identifiers, comments allowed by the exceptions below, commit messages,
  repository documentation and this file. Preserve user-provided monitor names and settings.
- Report observed facts separately from assumptions. Never claim a test, CI job, supported
  desktop or physical input switch passed without evidence.

## Read before changing anything

1. [README.md](README.md): product behavior, settings, platform requirements and branch policy.
2. [docs/architecture.md](docs/architecture.md): ownership, adapters and test boundaries.
3. [docs/protocol.md](docs/protocol.md) and [spec/protocol.json](spec/protocol.json): wire format
   and shared expected results.
4. [docs/linux.md](docs/linux.md) for Linux work, and the affected platform's source and tests.
5. [.github/workflows/ci.yml](.github/workflows/ci.yml) and
   [.github/workflows/release.yml](.github/workflows/release.yml) for packaging or automation.

## Ownership and architecture

| Location | Responsibility |
|---|---|
| `spec/protocol.json`, `spec/fixtures/` | Shared protocol, input, settings, manufacturer and EDID test vectors |
| `macos/Sources/UltraGearInputCore/` | Pure Swift protocol, display and settings logic |
| `macos/Sources/UltraGearInput/` | AppKit menu, IOKit transport, settings storage and login integration |
| `dotnet/src/UltraGearInput.Core/` | Platform-free C# protocol, settings, EDID, display identity, menu model and shared text |
| `dotnet/src/UltraGearInput.Windows/` | Windows Forms tray adapter and Windows lifecycle |
| `dotnet/src/UltraGearInput.Windows.Native/` | Win32, DXVA2, NVIDIA NVAPI and AMD ADL interop |
| `dotnet/src/UltraGearInput.Linux/` | Linux session D-Bus, tray, sysfs discovery and I2C adapters |
| `dotnet/tests/UltraGearInput.Tests/` | Full .NET unit suite, including Linux protocol and transport tests |
| `linux/packaging/`, `linux/scripts/`, `linux/tests/` | Installation, archives and desktop integration tests |

- Keep platform-free logic pure and independently testable. OS adapters translate inputs and
  results; they must not grow a second settings validator, frame builder or menu policy.
- Windows and Linux share `UltraGearInput.Core`, including `TrayMenu`, `SettingsStore` and
  `AppText`. Extend those abstractions before adding platform-specific copies.
- Swift remains native. Preserve semantic parity with C# through shared vectors; do not add a
  cross-language runtime to share implementation.
- The Linux app implements D-Bus directly and ships as a self-contained, trimmed executable.
  Preserve that deployment model. Discuss any new runtime dependency with the user first.
- Keep .NET versions centralized in `dotnet/Directory.Packages.props`, build settings in
  `dotnet/Directory.Build.props` and SDK selection in `dotnet/global.json`.
- Use small, focused functions and existing abstractions. Do not suppress compiler, analyzer,
  trimming or formatter failures to get a green build.
- Do not add source comments, JSDoc or docstrings. Required license notices, shebangs,
  `swift-tools-version` and directives with a necessary technical effect are exceptions.
  Explain non-obvious decisions in documentation; in the Arara workspace, use the private
  `ac-docs/docs/36-notas-de-codigo.md` record. Public usage and architecture docs must remain
  understandable without access to that workspace.

## Protocol and compatibility invariants

- Treat `spec/protocol.json` as the shared test contract. Change vectors only when the protocol
  or an intentional product requirement changes, with independent evidence for the new values.
  Never regenerate expected bytes from the implementation being tested.
- Standard DDC/CI uses source `0x51` and VCP code `0x60`; LG uses source `0x50` and code `0xF4`.
  Both target the 7-bit I2C address `0x37`. Keep the 8-bit destination byte `0x6E` distinct from
  that address and from EDID address `0x50`.
- Preserve the seven-byte Set VCP frame, value byte order and checksum: XOR `0x6E` with every
  preceding frame byte. Validate both protocols and every default input against the vectors.
- Preserve manufacturer decoding, LG default selection, explicit per-display overrides and
  stable display keys. Do not silently reset a person's protocol choice after hotplug.
- Keep settings backward compatible: camel-case JSON, optional custom inputs, at most 32 inputs,
  unique nonblank IDs, nonblank names and valid protocol/value ranges. Preserve user-defined
  order and names, atomic saves and the invalid-file fallback with a visible error.
- Keep `--list-displays` machine-readable JSON and `--version` consistent with `VERSION`.
  Diagnostics belong on stderr and must not corrupt structured stdout.
- A rename or removal of a settings field, CLI flag, exported D-Bus member, artifact name or
  discovery key requires updating every producer, consumer, test and affected guide together.

## Platform-specific requirements

### macOS

- Preserve macOS 14 or later and Apple silicon support, native AppKit menus and the existing
  IOKit `IOAVServiceWriteI2C` path. Do not imply Intel Mac support.
- Keep UI operations on the appropriate AppKit thread and release acquired native resources.
  Preserve login registration and the existing settings location.
- Build the app bundle with `macos/scripts/bundle.sh`. Do not claim signing or notarization
  unless it was performed and verified. Keep quarantine guidance accurate.

### Windows

- Preserve Windows 10/11 x64, native WinForms tray behavior and per-monitor DPI handling.
- Standard switching uses `SetVCPFeature`; raw LG switching uses the NVIDIA/AMD paths.
  Do not claim LG support on Intel graphics through the standard Windows API.
- Verify native calling conventions, struct packing, sizes, offsets, handle lifetimes and error
  propagation against the SDK contract. Keep interop layout regression tests.
- Preserve startup registration, cleanup of tray resources and the distinction between a
  cross-platform publish check and a smoke test executed on Windows.

### Linux

- Use the session bus, StatusNotifierItem and `com.canonical.dbusmenu`. Keep single-instance
  ownership, watcher discovery, delayed tray startup, re-registration and clean shutdown.
- Never await a round trip from inside the D-Bus read loop that must process its reply.
  Validate incoming signatures, lengths and indexes before reading or allocating; preserve
  alignment, byte order, message boundaries and reply/error correlation.
- Keep stable menu IDs for unchanged entries. Structure changes publish `LayoutUpdated` and
  increment the revision; property-only changes publish `ItemsPropertiesUpdated`. GNOME may
  defer updates until the menu opens, so integration checks must exercise that lifecycle.
- Resolve sysfs symlinks physically before traversing `..`. Preserve external-display filtering,
  EDID identity, connector-to-bus resolution and the NVIDIA EDID matching fallback.
- Write DDC/CI only through the resolved display bus at `0x37`; read EDID at `0x50`. Preserve
  the equivalent plain I2C and SMBus block-write frame semantics and release device handles.
- Run the application as a normal user. Keep system changes inside the explicit installer step;
  do not broaden the display-controller `uaccess` rule or make real I2C devices world-writable.
- Preserve XDG paths, autostart toggles and desktop entry escaping. Uninstall keeps settings
  unless `--purge` is explicit; system rule removal requires `--system`.
- Switching errors must reach the person through desktop notifications and stderr. A missing
  notification service must not turn a failed switch into a success claim.
- Keep shell scripts compatible with macOS Bash 3.2 where used by the build/test tooling,
  including empty arrays under `set -u`. Preserve exit codes through pipelines.

## Safety and permissions

- Builds, unit tests and isolated desktop tests may run within the authorized task. Actual
  monitor switching, bus probing beyond isolated fixtures, driver/module changes and system
  installation need task-specific authorization. Full filesystem access alone is not that
  authorization.
- Never scan arbitrary I2C buses or use `i2cset` against a real adapter as a generic smoke test.
  A wrong bus or address can affect devices unrelated to the display.
- Run `installer.sh --system` and kernel `i2c-stub` setup only on a disposable Linux runner/VM
  or an explicitly authorized host. `--system` changes real `/etc` files and kernel state even
  though the installer test isolates its home directory.
- Integration tests use disposable homes, session buses, fake sysfs trees and explicit device
  mappings. `ULTRAGEAR_INPUT_SYSFS` and `ULTRAGEAR_INPUT_DEV` are fixture overrides. Never pass
  a real graphics bus as the optional stub-bus argument.
- Do not run a privileged desktop or install the app into the user's live profile to verify a
  patch. Do not kill unrelated processes, erase settings or delete locks without proving ownership.
- Keep this utility local. Adding telemetry, network services, remote code, elevated runtime
  execution or automatic downloads requires an explicit product decision.
- Preserve [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Record the
  origin and required attribution whenever third-party code is introduced.

## Tests and verification

Every behavior change includes a regression test in the same change. A bug fix must have a test
that fails before the fix and passes afterward. Cover the happy path, malformed input, failure
paths and meaningful boundaries. Mock OS/transport boundaries, not the logic under test. Never
remove, skip or weaken tests to hide a failure; reproduce an alleged pre-existing failure on the
baseline before classifying it that way.

Run the full suite for each affected implementation. Shared protocol/settings changes require
both Swift and .NET suites. Native behavior also needs the affected platform's real integration
check; publishing on macOS does not prove Windows or Linux runtime behavior.

From `macos/`:

```sh
swift format lint --strict --recursive --configuration .swift-format Sources Tests Package.swift
swift build -Xswiftc -warnings-as-errors
swift test
./scripts/bundle.sh
"dist/UltraGear Input.app/Contents/MacOS/UltraGearInput" --list-displays
```

From `dotnet/` with the .NET 10 SDK selected by `global.json`:

```sh
dotnet format UltraGearInput.slnx --verify-no-changes
dotnet build UltraGearInput.slnx --configuration Release
dotnet test --solution UltraGearInput.slnx --configuration Release --no-build
```

The solution uses Microsoft.Testing.Platform; preserve the `--solution` invocation. For Windows
packaging, run from `dotnet/` and execute the resulting binary on Windows:

```sh
dotnet publish src/UltraGearInput.Windows/UltraGearInput.Windows.csproj --configuration Release --output publish
```

From the repository root, Linux packaging and desktop checks are:

```sh
linux/scripts/package.sh x64 arm64
linux/tests/installer.sh <archive.tar.gz>
linux/tests/run-in-container.sh <linux-binary> <isolated-screenshot-directory>
```

The binary and archive must match the execution host's architecture. Packaging replaces
`linux/dist/`; preserve any artifacts needed before running it. The container test needs Docker
and runs the real app with GNOME Shell/AppIndicator and Xfce. Its fake sysfs tree does not prove a
physical monitor switched. The optional third argument is exclusively a disposable kernel
`i2c-stub` bus; that path checks SMBus writes, not a real graphics adapter's plain I2C path.

For a documentation-only change, check the diff, relative links, referenced paths and command
accuracy. Product builds and hardware tests are unnecessary unless executable behavior changed.
Report that distinction explicitly.

## Git, CI and releases

- `main` is stable; `development` is daily integration. `feature/*` and `fix/*` start from and
  return to `development`; promotion to `main` uses a PR. Preserve the current checkout and work.
- Commit, push, merge, tag and publish only when the user has authorized the corresponding
  action. Writing documentation or saying "finish" does not independently authorize a release.
- Use Conventional Commits in English. Do not add AI authorship/co-authorship or signatures.
  Never force-push, rewrite history, use `reset --hard` or bypass hooks without explicit approval.
- Check actual PR/run state. Queued, canceled, missing or skipped checks are not passing checks.
  Give the run link and remaining blocker; do not silently merge or weaken CI to bypass it.
- CI runs on pushes and PRs to `main`/`development`, and supports manual/reusable execution.
  Preserve macOS, Windows and Linux checks plus x64/arm64 Linux desktop integration. This
  personal project's workflow is distinct from the Arara product workflows.
- Development builds remain artifacts with SHA-256 and 14-day retention. Keep screenshot
  artifacts separate from distributable packages.
- `VERSION` is the release version source. A `vX.Y.Z` release tag must match it and refer to a
  commit on `main`; all required build/integration jobs must pass before publication.
- Preserve release filenames, checksums, installer contents, executable permissions and the
  release guard. Publish only distributable archives/executables and their checksums, with no
  screenshots, logs, test fixtures, private files or credentials.
- After an authorized merge, verify the remote result before removing only the merged temporary
  branch. Never delete a permanent branch or unmerged work. Report a pending merge explicitly.

## Completion criteria

A task is ready when the requested behavior/document exists, affected contracts and docs agree,
and the relevant checks have actual results. Report changed files, what was verified, failures,
checks not run and remaining hardware/platform limits. Include a concrete command or short
manual test for the user. Do not present historical test counts as a fresh run.

In the Arara workspace, record changes in `ac-docs/STATUS.md` and the relevant private component
document. Keep this repository's rules synchronized with its workspace summary without copying
private documentation into the public project. If the user requests a complete security audit,
agree on a private report location before writing any report into this public repository.
