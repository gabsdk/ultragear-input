import AppKit
import ServiceManagement
import UltraGearInputCore

@MainActor
final class StatusMenuController: NSObject, NSMenuDelegate {
    private struct InputSelection {
        let display: DisplayDescriptor
        let input: InputSource
        let ddcProtocol: DDCProtocol
    }

    private struct ProtocolSelection {
        let display: DisplayDescriptor
        let ddcProtocol: DDCProtocol
    }

    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let menu = NSMenu()
    private let store: SettingsStore
    private var updateTask: Task<Void, Never>?
    private var updateOffer: UpdateRelease?
    private var updateBusy = false

    init(store: SettingsStore = .standard) {
        self.store = store
        super.init()
        statusItem.button?.image = NSImage(systemSymbolName: "display", accessibilityDescription: Copy.appName)
        statusItem.button?.toolTip = Copy.appName
        menu.delegate = self
        menu.autoenablesItems = false
        statusItem.menu = menu
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let loaded = store.load()
        let settings = (try? loaded.get()) ?? Settings()
        let displays = DisplayDiscovery.descriptors()

        if displays.isEmpty {
            menu.addItem(disabledItem(Copy.noDisplays))
        }
        for display in displays {
            addSection(for: display, settings: settings)
        }

        menu.addItem(.separator())
        if case .failure(let error) = loaded {
            menu.addItem(disabledItem(Copy.invalidSettings(error)))
        }
        menu.addItem(actionItem(Copy.openSettings, action: #selector(openSettings)))
        let loginItem = actionItem(Copy.launchAtLogin, action: #selector(toggleLaunchAtLogin))
        loginItem.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(loginItem)
        menu.addItem(.separator())
        let updateLabel = UpdateText.get(
            updateBusy ? (updateOffer == nil ? "checking" : "installing") : (updateOffer == nil ? "check" : "install"),
            value: updateOffer?.version.description)
        let updateItem = actionItem(updateLabel, action: #selector(updateApp))
        updateItem.isEnabled = !updateBusy
        menu.addItem(updateItem)
        menu.addItem(actionItem(Copy.quit, action: #selector(quit), keyEquivalent: "q"))
    }

    private func addSection(for display: DisplayDescriptor, settings: Settings) {
        let ddcProtocol = settings.ddcProtocol(for: display)
        menu.addItem(.sectionHeader(title: display.name))
        for input in settings.resolvedInputs {
            let item = actionItem(input.name, action: #selector(selectInput(_:)))
            item.representedObject = InputSelection(display: display, input: input, ddcProtocol: ddcProtocol)
            item.indentationLevel = 1
            menu.addItem(item)
        }
        let protocolItem = NSMenuItem(title: Copy.protocolMenu, action: nil, keyEquivalent: "")
        protocolItem.indentationLevel = 1
        let submenu = NSMenu()
        for option in DDCProtocol.allCases {
            let item = actionItem(Copy.protocolName(option), action: #selector(selectProtocol(_:)))
            item.representedObject = ProtocolSelection(display: display, ddcProtocol: option)
            item.state = option == ddcProtocol ? .on : .off
            submenu.addItem(item)
        }
        protocolItem.submenu = submenu
        menu.addItem(protocolItem)
    }

    @objc private func selectInput(_ sender: NSMenuItem) {
        guard let selection = sender.representedObject as? InputSelection else { return }
        let key = selection.display.key
        let value = selection.input.value(for: selection.ddcProtocol)
        let ddcProtocol = selection.ddcProtocol
        Task {
            do throws(DDCError) {
                try await InputSwitcher.shared.switchInput(displayKey: key, value: value, ddcProtocol: ddcProtocol)
            } catch {
                presentAlert(Copy.switchFailed, Copy.describe(error))
            }
        }
    }

    @objc private func selectProtocol(_ sender: NSMenuItem) {
        guard let selection = sender.representedObject as? ProtocolSelection else { return }
        do {
            let current = try store.load().get()
            try store.save(current.setting(selection.ddcProtocol, for: selection.display))
        } catch let error as SettingsError {
            presentAlert(Copy.settingsFailed, Copy.invalidSettings(error))
        } catch {
            presentAlert(Copy.settingsFailed, error.localizedDescription)
        }
    }

    @objc private func openSettings() {
        do {
            try store.ensureFileExists()
            NSWorkspace.shared.open(store.fileURL)
        } catch {
            presentAlert(Copy.settingsFailed, error.localizedDescription)
        }
    }

    @objc private func toggleLaunchAtLogin() {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            presentAlert(Copy.loginItemFailed, error.localizedDescription)
        }
    }

    @objc private func updateApp() {
        guard !updateBusy else { return }
        updateBusy = true
        updateTask = Task {
            defer {
                updateBusy = false
                updateTask = nil
            }
            do {
                if let offer = updateOffer {
                    try await UpdateInstaller.prepare(offer, target: Bundle.main.bundleURL)
                    try Task.checkCancellation()
                    NSApp.terminate(nil)
                } else {
                    guard let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String else {
                        throw UpdateFailure.unsupported
                    }
                    let latest = try await UpdateClient().check(currentVersion: version)
                    try Task.checkCancellation()
                    if let latest {
                        updateOffer = latest
                        presentAlert(Copy.appName, UpdateText.get("available", value: latest.version.description))
                    } else {
                        presentAlert(Copy.appName, UpdateText.get("current", value: version))
                    }
                }
            } catch is CancellationError {
            } catch {
                if !Task.isCancelled {
                    presentAlert(
                        UpdateText.get("failed"),
                        UpdateText.get((error as? UpdateFailure)?.rawValue ?? (error is URLError ? "network" : "installFailed")))
                }
            }
        }
    }

    func cancelUpdate() {
        updateTask?.cancel()
    }

    @objc private func quit() {
        cancelUpdate()
        NSApp.terminate(nil)
    }

    private func actionItem(_ title: String, action: Selector, keyEquivalent: String = "") -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: keyEquivalent)
        item.target = self
        return item
    }

    private func disabledItem(_ title: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.isEnabled = false
        return item
    }

    private func presentAlert(_ title: String, _ message: String) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = title
        alert.informativeText = message
        alert.addButton(withTitle: UpdateText.get("ok"))
        NSApp.activate()
        alert.runModal()
    }
}
