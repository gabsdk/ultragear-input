#!/usr/bin/env python3
import argparse
import ast
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "spec" / "fixtures"
PROTOCOL = json.loads((ROOT / "spec" / "protocol.json").read_text())
VERSION = (ROOT / "VERSION").read_text().strip()
WATCHER = "org.kde.StatusNotifierWatcher"
INSTANCE = "dev.gabsdk.UltraGearInput"
MISSING_LG_BUS = 250
MISSING_DELL_BUS = 251
SESSION_FLAG = "ULTRAGEAR_TEST_IN_SESSION"

FIND_INDICATOR = """
const indicator = Object.values(Main.panel.statusArea).find(item => item?._indicator?.id === 'ultragear-input');
"""

DESCRIBE_MENU = FIND_INDICATOR + """
const describe = item => ({
  kind: item.constructor.name,
  label: item.label ? item.label.text : '',
  ornament: item._ornament ?? 0,
  sensitive: item.reactive,
  children: item.menu && item.menu._getMenuItems ? item.menu._getMenuItems().map(describe) : [],
});
indicator ? indicator.menu._getMenuItems().map(describe) : null;
"""

DESCRIBE_INDICATOR = FIND_INDICATOR + """
indicator ? {
  visible: indicator.visible,
  mapped: indicator.mapped,
  ready: indicator._indicator.isReady,
  iconNames: indicator._icon.gicon?.get_icon?.()?.get_names?.() ?? [],
  iconWidth: indicator._icon.width,
} : null;
"""

OPEN_MENU = FIND_INDICATOR + "indicator.menu.open(); true;"

CLOSE_MENU = FIND_INDICATOR + "indicator.menu.close(); true;"

NOTIFICATIONS = """
Main.messageTray.getSources().flatMap(source => source.notifications.map(n => ({ source: source.title, title: n.title, body: n.body })));
"""


def click_script(header, label, submenu=None):
    return FIND_INDICATOR + f"""
const items = indicator.menu._getMenuItems();
const start = items.findIndex(item => item.label?.text === {json.dumps(header)});
let target = null;
if (start >= 0) {{
  const scope = {json.dumps(submenu)} === null
    ? items.slice(start)
    : items.slice(start).find(item => item.label?.text === {json.dumps(submenu)})?.menu._getMenuItems() ?? [];
  target = scope.find(item => item.label?.text === {json.dumps(label)});
}}
if (target) target._dbusItem.handleEvent('clicked', null, 0);
!!target;
"""


class Results:
    def __init__(self):
        self.failures = []
        self.passes = 0

    def check(self, name, condition, detail=""):
        if condition:
            self.passes += 1
            print(f"PASS {name}", flush=True)
        else:
            self.failures.append(name)
            print(f"FAIL {name} {detail}".rstrip(), flush=True)
        return condition

    def summary(self):
        print(f"\n{self.passes} passed, {len(self.failures)} failed", flush=True)
        for name in self.failures:
            print(f"  FAIL {name}")
        return 1 if self.failures else 0


def wait_for(predicate, timeout=20.0, interval=0.25):
    deadline = time.monotonic() + timeout
    value = predicate()
    while not value and time.monotonic() < deadline:
        time.sleep(interval)
        value = predicate()
    return value


def gdbus(*arguments):
    completed = subprocess.run(["gdbus", "call", "--session", *arguments], capture_output=True, text=True, timeout=30)
    return completed.stdout.strip() if completed.returncode == 0 else None


def shell_eval(script):
    output = gdbus("--dest", "org.gnome.Shell", "--object-path", "/org/gnome/Shell", "--method", "org.gnome.Shell.Eval", script)
    if not output:
        return None
    match = re.match(r"^\((true|false), (.*)\)$", output, re.S)
    if not match or match.group(1) != "true":
        return None
    text = ast.literal_eval(match.group(2))
    return json.loads(text) if text else None


def registered_items():
    output = gdbus("--dest", WATCHER, "--object-path", "/StatusNotifierWatcher", "--method",
                   "org.freedesktop.DBus.Properties.Get", WATCHER, "RegisteredStatusNotifierItems")
    return re.findall(r"'([^']+)'", output or "")


def screenshot(directory, name):
    if directory:
        gdbus("--dest", "org.gnome.Shell.Screenshot", "--object-path", "/org/gnome/Shell/Screenshot",
              "--method", "org.gnome.Shell.Screenshot.Screenshot", "false", "false", str(Path(directory) / f"{name}.png"))
        time.sleep(1)


def frame(protocol, input_id):
    return next(item["bytes"] for item in PROTOCOL["frames"] if item["protocol"] == protocol and item["inputId"] == input_id)


def read_stub_registers(bus, first, count):
    values = []
    for register in range(first, first + count):
        completed = subprocess.run(["i2cget", "-y", str(bus), "0x37", hex(register)], capture_output=True, text=True)
        if completed.returncode != 0:
            return None
        values.append(int(completed.stdout.strip(), 16))
    return values


def build_sysfs(root, lg_bus):
    gpu = root / "devices" / "pci0000:00" / "0000:03:00.0"
    (gpu).mkdir(parents=True)
    (gpu / "class").write_text("0x030000\n")
    (root / "class" / "drm").mkdir(parents=True)
    connectors = [
        ("card1-DP-1", "lg-ultragear.edid", lg_bus),
        ("card1-HDMI-A-1", "dell-u2723qe.edid", MISSING_DELL_BUS),
        ("card1-eDP-1", "lg-ultragear.edid", 1),
    ]
    for name, fixture, bus in connectors:
        connector = gpu / "drm" / "card1" / name
        connector.mkdir(parents=True)
        (connector / "status").write_text("connected\n")
        shutil.copy(FIXTURES / fixture, connector / "edid")
        (gpu / f"i2c-{bus}").mkdir(exist_ok=True)
        (connector / "ddc").symlink_to(f"../../../i2c-{bus}")
        (root / "class" / "drm" / name).symlink_to(os.path.relpath(connector, root / "class" / "drm"))


def find_extension():
    for base in ("/usr/share/gnome-shell/extensions", os.path.expanduser("~/.local/share/gnome-shell/extensions")):
        if os.path.isdir(base):
            for name in sorted(os.listdir(base)):
                if "appindicator" in name:
                    return name
    return None


def describe_open_menu():
    shell_eval(OPEN_MENU)
    time.sleep(0.5)
    described = shell_eval(DESCRIBE_MENU)
    shell_eval(CLOSE_MENU)
    return described


def labels(menu):
    return [item["label"] for item in menu]


def section(menu, header):
    start = labels(menu).index(header)
    end = next((index for index in range(start + 1, len(menu)) if menu[index]["kind"] == "PopupSeparatorMenuItem"), len(menu))
    return menu[start:end]


def ornament(menu, header, label):
    protocol = next(item for item in section(menu, header) if item["label"] == "Protocol")
    return next(child["ornament"] for child in protocol["children"] if child["label"] == label)


def notifications_with(text):
    return [item for item in shell_eval(NOTIFICATIONS) or [] if text in (item.get("body") or "")]


def run_xfce(arguments, results, binary, app_environment, workspace):
    home_config = Path(os.environ["XDG_CONFIG_HOME"]) / "xfce4" / "xfconf" / "xfce-perchannel-xml"
    home_config.mkdir(parents=True, exist_ok=True)
    shutil.copy("/etc/xdg/xfce4/panel/default.xml", home_config / "xfce4-panel.xml")
    display = ":42"
    xvfb = subprocess.Popen(["Xvfb", display, "-screen", "0", "1280x720x24"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    environment = dict(os.environ, DISPLAY=display)
    monitor_log = open(workspace / "xfce-monitor.log", "w")
    processes = []
    try:
        time.sleep(1)
        processes.append(subprocess.Popen(["dbus-monitor", "--session", "interface='com.canonical.dbusmenu'"], stdout=monitor_log, stderr=subprocess.STDOUT))
        processes.append(subprocess.Popen(["xfce4-panel"], env=environment, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
        app = subprocess.Popen([binary], env=dict(app_environment, DISPLAY=display), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        processes.append(app)
        results.check("XFCE: registers with the xfce4-panel tray",
                      bool(wait_for(lambda: any(item.startswith(f"org.kde.StatusNotifierItem-{app.pid}-1") for item in registered_items()), timeout=30)),
                      registered_items())
        results.check("XFCE: the panel reads the menu layout",
                      bool(wait_for(lambda: "member=GetLayout" in (workspace / "xfce-monitor.log").read_text(), timeout=20)))
        if arguments.screenshots:
            time.sleep(1)
            subprocess.run(["import", "-display", display, "-window", "root", str(Path(arguments.screenshots) / "xfce-top-panel.png")], check=False)
        app.terminate()
        results.check("XFCE: stops on SIGTERM", app.wait(timeout=10) == 0)
    finally:
        for process in reversed(processes):
            if process.poll() is None:
                process.kill()
        xvfb.kill()
        monitor_log.close()


def run(arguments):
    results = Results()
    workspace = Path(os.environ["ULTRAGEAR_TEST_ROOT"])
    sysfs = workspace / "sys"
    config = Path(os.environ["XDG_CONFIG_HOME"])
    data = Path(os.environ["XDG_DATA_HOME"])
    lg_bus = arguments.stub_bus if arguments.stub_bus is not None else MISSING_LG_BUS
    build_sysfs(sysfs, lg_bus)
    binary = str(Path(arguments.binary).resolve())
    app_environment = dict(os.environ, ULTRAGEAR_INPUT_SYSFS=str(sysfs))

    version = subprocess.run([binary, "--version"], capture_output=True, text=True, env=app_environment)
    results.check("prints the release version", version.returncode == 0 and version.stdout.strip() == VERSION, version.stdout)

    listing = subprocess.run([binary, "--list-displays"], capture_output=True, text=True, env=app_environment)
    displays = json.loads(listing.stdout) if listing.returncode == 0 else []
    lg = PROTOCOL["edids"][0]
    results.check("lists external displays from sysfs and skips the internal panel",
                  [(d["name"], d["protocol"]) for d in displays] == [("LG ULTRAGEAR", "lg"), ("DELL U2723QE", "standard")], listing.stdout)
    results.check("derives a stable key from the EDID", displays[:1] and displays[0]["key"] == "edid-" + lg["identity"], listing.stdout)

    launcher = subprocess.run([binary, "--install-launcher"], capture_output=True, text=True, env=app_environment)
    launcher_file = data / "applications" / "ultragear-input.desktop"
    results.check("installs the applications launcher",
                  launcher.returncode == 0 and f'Exec="{binary}"' in launcher_file.read_text(), launcher.stderr)

    if shutil.which("xfce4-panel") and shutil.which("Xvfb"):
        run_xfce(arguments, results, binary, app_environment, workspace)

    app_log = open(workspace / "app.log", "w")
    app = subprocess.Popen([binary], stdout=app_log, stderr=subprocess.STDOUT, env=app_environment)
    results.check("owns the single-instance bus name before any tray host exists",
                  wait_for(lambda: "true" in (gdbus("--dest", "org.freedesktop.DBus", "--object-path", "/org/freedesktop/DBus",
                                                     "--method", "org.freedesktop.DBus.NameHasOwner", INSTANCE) or "")))

    second = subprocess.run([binary], capture_output=True, text=True, env=app_environment, timeout=15)
    results.check("refuses a second instance", second.returncode == 0 and "already running" in second.stderr, second.stderr)

    extension = find_extension()
    results.check("finds the AppIndicator GNOME Shell extension", extension is not None)
    subprocess.run(["gsettings", "set", "org.gnome.shell", "enabled-extensions", json.dumps([extension]).replace('"', "'")], check=True)
    subprocess.run(["gsettings", "set", "org.gnome.shell", "welcome-dialog-last-shown-version", "'999'"], check=False)
    shell_log = open(workspace / "gnome-shell.log", "w")
    shell = subprocess.Popen(["gnome-shell", "--headless", "--unsafe-mode", "--virtual-monitor", "1280x720", "--wayland", "--no-x11"],
                             stdout=shell_log, stderr=subprocess.STDOUT)
    try:
        item_name = f"org.kde.StatusNotifierItem-{app.pid}-1"
        results.check("registers with the watcher once GNOME Shell starts later",
                      wait_for(lambda: item_name in registered_items(), timeout=60), registered_items())
        shell_eval("Main.overview.hide(); true")

        indicator = wait_for(lambda: (state := shell_eval(DESCRIBE_INDICATOR)) and state["ready"] and state["iconWidth"] > 0 and state, timeout=30)
        results.check("shows a ready, visible icon in the top bar",
                      bool(indicator) and indicator["visible"] and indicator["mapped"], indicator)
        results.check("uses the themed display icon", bool(indicator) and "video-display-symbolic" in indicator["iconNames"], indicator)
        screenshot(arguments.screenshots, "top-bar")

        menu = wait_for(lambda: (described := shell_eval(DESCRIBE_MENU)) and len(described) > 10 and described, timeout=30) or []
        expected_inputs = [item["name"] for item in PROTOCOL["defaultInputs"]]
        results.check("renders the full menu inside GNOME Shell", labels(menu) == [
            "LG ULTRAGEAR", *expected_inputs, "Protocol", "",
            "DELL U2723QE", *expected_inputs, "Protocol", "",
            "Open Settings File…", "Start at Login", "", "Check for Updates…", "Quit UltraGear Input"], labels(menu))
        results.check("renders display names as non-clickable headers",
                      bool(menu) and not menu[0]["sensitive"] and menu[1]["sensitive"], menu[:2])
        protocol = next((item for item in menu if item["label"] == "Protocol"), None)
        results.check("renders Protocol as a submenu", bool(protocol) and protocol["kind"] == "PopupSubMenuMenuItem"
                      and labels(protocol["children"]) == ["Standard DDC/CI", "LG"], protocol)
        results.check("marks the LG protocol on the LG display",
                      bool(menu) and ornament(menu, "LG ULTRAGEAR", "LG") != ornament(menu, "LG ULTRAGEAR", "Standard DDC/CI"))
        shell_eval(OPEN_MENU)
        time.sleep(1)
        screenshot(arguments.screenshots, "menu-open")
        shell_eval(CLOSE_MENU)

        results.check("clicks LG HDMI 1 through the extension", shell_eval(click_script("LG ULTRAGEAR", "HDMI 1")) is True)
        if arguments.stub_bus is not None:
            expected = frame("lg", "hdmi1")
            written = wait_for(lambda: read_stub_registers(arguments.stub_bus, expected[0], 6) == expected[1:], timeout=10)
            results.check("writes the LG frame to the I2C bus", bool(written), read_stub_registers(arguments.stub_bus, expected[0], 6))
        else:
            results.check("explains a missing I2C device",
                          bool(wait_for(lambda: notifications_with(f"/dev/i2c-{MISSING_LG_BUS} does not exist"), timeout=10)),
                          shell_eval(NOTIFICATIONS))

        results.check("clicks Dell HDMI 1 through the extension", shell_eval(click_script("DELL U2723QE", "HDMI 1")) is True)
        results.check("notifies when the Dell bus is missing",
                      bool(wait_for(lambda: notifications_with(f"/dev/i2c-{MISSING_DELL_BUS} does not exist"), timeout=10)),
                      shell_eval(NOTIFICATIONS))

        results.check("chooses Standard DDC/CI for the LG display",
                      shell_eval(click_script("LG ULTRAGEAR", "Standard DDC/CI", submenu="Protocol")) is True)
        settings_file = config / "ultragear-input" / "settings.json"
        saved = wait_for(lambda: settings_file.exists() and json.loads(settings_file.read_text()).get("protocols"), timeout=10)
        results.check("saves the protocol override", saved == {"edid-" + lg["identity"]: "standard"}, saved)
        updated = wait_for(lambda: (described := describe_open_menu()) and described
                           and ornament(described, "LG ULTRAGEAR", "Standard DDC/CI") == ornament(menu, "LG ULTRAGEAR", "LG")
                           and described, timeout=15)
        results.check("GNOME Shell shows the new protocol after reopening the menu", bool(updated))
        if arguments.stub_bus is not None:
            shell_eval(click_script("LG ULTRAGEAR", "HDMI 1"))
            expected = frame("standard", "hdmi1")
            written = wait_for(lambda: read_stub_registers(arguments.stub_bus, expected[0], 6) == expected[1:], timeout=10)
            results.check("writes the standard frame after the protocol change", bool(written),
                          read_stub_registers(arguments.stub_bus, expected[0], 6))

        results.check("clicks Start at Login", shell_eval(click_script("Open Settings File…", "Start at Login")) is True)
        autostart = config / "autostart" / "ultragear-input.desktop"
        results.check("writes the autostart entry", wait_for(autostart.exists, timeout=10)
                      and f'Exec="{binary}"' in autostart.read_text())
        checked = wait_for(lambda: (described := describe_open_menu()) and next(
            (item["ornament"] for item in described if item["label"] == "Start at Login"), 0) != 0, timeout=15)
        results.check("shows Start at Login as checked", bool(checked))
        shell_eval(click_script("Open Settings File…", "Start at Login"))
        results.check("removes the autostart entry on the second click", bool(wait_for(lambda: not autostart.exists(), timeout=10)))

        results.check("clicks Quit", shell_eval(click_script("Open Settings File…", "Quit UltraGear Input")) is True)
        try:
            exit_code = app.wait(timeout=10)
        except subprocess.TimeoutExpired:
            exit_code = None
        results.check("exits cleanly from Quit", exit_code == 0, exit_code)
        results.check("disappears from the top bar", bool(wait_for(lambda: shell_eval(DESCRIBE_INDICATOR) is None, timeout=15)))
        screenshot(arguments.screenshots, "after-quit")
    finally:
        if app.poll() is None:
            app.kill()
        shell.terminate()
        try:
            shell.wait(timeout=10)
        except subprocess.TimeoutExpired:
            shell.kill()
        app_log.close()
        shell_log.close()
        if results.failures:
            print("\n--- app.log ---\n" + (workspace / "app.log").read_text())
            print("--- gnome-shell.log (last 40 lines) ---\n" + "\n".join((workspace / "gnome-shell.log").read_text().splitlines()[-40:]))
    return results.summary()


def main():
    parser = argparse.ArgumentParser(description="Real GNOME Shell test for the UltraGear Input Linux tray app.")
    parser.add_argument("--binary", required=True, help="Published ultragear-input executable")
    parser.add_argument("--stub-bus", type=int, help="i2c-stub bus number with a chip at 0x37; enables byte checks")
    parser.add_argument("--screenshots", help="Directory for PNG screenshots")
    arguments = parser.parse_args()
    if arguments.screenshots:
        Path(arguments.screenshots).mkdir(parents=True, exist_ok=True)
        arguments.screenshots = str(Path(arguments.screenshots).resolve())

    if os.environ.get(SESSION_FLAG) != "1":
        root = Path(tempfile.mkdtemp(prefix="ultragear-test-"))
        runtime = root / "runtime"
        runtime.mkdir(mode=0o700)
        environment = dict(
            os.environ,
            **{SESSION_FLAG: "1"},
            ULTRAGEAR_TEST_ROOT=str(root),
            HOME=str(root / "home"),
            XDG_CONFIG_HOME=str(root / "config"),
            XDG_DATA_HOME=str(root / "data"),
            XDG_CACHE_HOME=str(root / "cache"),
            XDG_STATE_HOME=str(root / "state"),
            XDG_RUNTIME_DIR=str(runtime),
            LANG="C.UTF-8",
            LC_ALL="C.UTF-8",
        )
        for variable in ("DBUS_SESSION_BUS_ADDRESS", "WAYLAND_DISPLAY", "DISPLAY", "XDG_CURRENT_DESKTOP"):
            environment.pop(variable, None)
        (root / "home").mkdir()
        try:
            completed = subprocess.run(["dbus-run-session", "--", sys.executable, __file__, *sys.argv[1:]], env=environment)
            return completed.returncode
        finally:
            shutil.rmtree(root, ignore_errors=True)
    return run(arguments)


if __name__ == "__main__":
    sys.exit(main())
