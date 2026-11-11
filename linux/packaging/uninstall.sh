#!/usr/bin/env bash
set -euo pipefail

readonly app_id="ultragear-input"
readonly rules_target="/etc/udev/rules.d/60-ultragear-input.rules"
readonly modules_target="/etc/modules-load.d/ultragear-input.conf"

prefix="${HOME}/.local"
remove_system=false
purge=false

usage() {
    cat <<EOF
Usage: ./uninstall.sh [--prefix DIR] [--system] [--purge]

  --prefix DIR   Directory used at install time (default: ~/.local)
  --system       Also remove the udev rule and the i2c-dev module setting (sudo)
  --purge        Also delete your settings file
EOF
}

while (($# > 0)); do
    case "$1" in
        --prefix) prefix="${2:?--prefix needs a directory}"; shift 2 ;;
        --system) remove_system=true; shift ;;
        --purge) purge=true; shift ;;
        -h | --help) usage; exit 0 ;;
        *) usage >&2; exit 2 ;;
    esac
done

config_home="${XDG_CONFIG_HOME:-${HOME}/.config}"
data_home="${XDG_DATA_HOME:-${HOME}/.local/share}"

pkill -u "$(id -u)" -x "${app_id}" || true
rm -f "${prefix}/bin/${app_id}" "${data_home}/applications/${app_id}.desktop" "${config_home}/autostart/${app_id}.desktop"
echo "Removed the program, the launcher and Start at Login"

if [[ "${purge}" == true ]]; then
    rm -f "${config_home}/${app_id}/settings.json"
    rmdir "${config_home}/${app_id}" 2> /dev/null || true
    echo "Removed the settings file"
fi

if [[ "${remove_system}" == true ]]; then
    sudo_command=()
    if [[ "$(id -u)" -ne 0 ]]; then
        sudo_command=(sudo)
    fi
    ${sudo_command[@]+"${sudo_command[@]}"} rm -f "${rules_target}" "${modules_target}"
    ${sudo_command[@]+"${sudo_command[@]}"} udevadm control --reload-rules || true
    echo "Removed ${rules_target} and ${modules_target}"
fi
