#!/usr/bin/env bash
set -euo pipefail

readonly app_id="ultragear-input"
readonly rules_target="/etc/udev/rules.d/60-ultragear-input.rules"
readonly modules_target="/etc/modules-load.d/ultragear-input.conf"

package_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
prefix="${HOME}/.local"
system_setup=true
start_app=true
autostart=false

usage() {
    cat <<EOF
Usage: ./install.sh [--prefix DIR] [--autostart] [--no-system] [--no-start]

Installs UltraGear Input for the current user.

  --prefix DIR   Install the program to DIR/bin (default: ~/.local)
  --autostart    Start UltraGear Input when you log in
  --no-system    Skip the sudo step that grants access to the monitor I2C buses
  --no-start     Do not start the app after installing
EOF
}

while (($# > 0)); do
    case "$1" in
        --prefix) prefix="${2:?--prefix needs a directory}"; shift 2 ;;
        --autostart) autostart=true; shift ;;
        --no-system) system_setup=false; shift ;;
        --no-start) start_app=false; shift ;;
        -h | --help) usage; exit 0 ;;
        *) usage >&2; exit 2 ;;
    esac
done

if [[ "$(id -u)" -eq 0 && -z "${ULTRAGEAR_ALLOW_ROOT:-}" ]]; then
    echo "Run install.sh as your normal user. It asks for sudo only for the I2C setup." >&2
    exit 1
fi

config_home="${XDG_CONFIG_HOME:-${HOME}/.config}"
data_home="${XDG_DATA_HOME:-${HOME}/.local/share}"
binary="${prefix}/bin/${app_id}"

echo "Installing ${binary}"
install -D -m 755 "${package_dir}/${app_id}" "${binary}"
echo "Adding the app launcher: $("${binary}" --install-launcher)"

if [[ "${autostart}" == true ]]; then
    install -D -m 644 "${data_home}/applications/${app_id}.desktop" "${config_home}/autostart/${app_id}.desktop"
    echo "Enabled Start at Login"
fi

if [[ "${system_setup}" == true ]]; then
    sudo_command=()
    if [[ "$(id -u)" -ne 0 ]]; then
        sudo_command=(sudo)
    fi
    echo "Granting your session access to the graphics card I2C buses (sudo):"
    echo "  ${rules_target}"
    echo "  ${modules_target}"
    ${sudo_command[@]+"${sudo_command[@]}"} install -D -m 644 "${package_dir}/60-ultragear-input.rules" "${rules_target}"
    ${sudo_command[@]+"${sudo_command[@]}"} install -D -m 644 "${package_dir}/ultragear-input.conf" "${modules_target}"
    ${sudo_command[@]+"${sudo_command[@]}"} modprobe i2c-dev
    ${sudo_command[@]+"${sudo_command[@]}"} udevadm control --reload-rules
    ${sudo_command[@]+"${sudo_command[@]}"} udevadm trigger --action=add --subsystem-match=i2c-dev
    ${sudo_command[@]+"${sudo_command[@]}"} udevadm settle || true
fi

if [[ "${XDG_CURRENT_DESKTOP:-}" == *GNOME* ]] && command -v gnome-extensions > /dev/null; then
    if ! gnome-extensions list --enabled 2> /dev/null | grep -qi appindicator; then
        cat <<'EOF'

GNOME needs the AppIndicator extension to show the icon in the top bar.
  Ubuntu:        sudo apt install gnome-shell-extension-appindicator
  Fedora:        sudo dnf install gnome-shell-extension-appindicator
  Debian:        sudo apt install gnome-shell-extension-appindicator
  Arch:          sudo pacman -S gnome-shell-extension-appindicator
Then log out and back in, and enable it:
  gnome-extensions enable appindicatorsupport@rgcjonas.gmail.com
EOF
    fi
fi

if [[ "${start_app}" == true ]]; then
    if pgrep -u "$(id -u)" -x "${app_id}" > /dev/null; then
        echo "UltraGear Input is already running. Quit it from the menu and start it again to use the new version."
    else
        setsid "${binary}" > /dev/null 2>&1 < /dev/null &
        echo "Started UltraGear Input. Look for the display icon in the top bar."
    fi
fi

echo
echo "Check what the app sees with: ${binary} --list-displays"
