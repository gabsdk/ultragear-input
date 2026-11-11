#!/usr/bin/env bash
set -euo pipefail

tests_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${tests_dir}/kernel.sh"

archive="${1:?Usage: installer.sh <ultragear-input-*.tar.gz> [--system]}"
system_mode="${2:-}"
failures=0

check() {
    local name="$1"
    shift
    if "$@"; then
        echo "PASS ${name}"
    else
        echo "FAIL ${name}"
        failures=$((failures + 1))
    fi
}

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
export HOME="${work}/home" XDG_CONFIG_HOME="${work}/home/.config" XDG_DATA_HOME="${work}/home/.local/share"
unset XDG_CURRENT_DESKTOP
mkdir -p "${HOME}"
tar -xzf "${archive}" -C "${work}"
package="$(find "${work}" -maxdepth 1 -type d -name 'ultragear-input-*' | head -n 1)"

check "archive contains the installer files" test -x "${package}/ultragear-input" -a -x "${package}/install.sh" -a -x "${package}/uninstall.sh" \
    -a -f "${package}/60-ultragear-input.rules" -a -f "${package}/ultragear-input.conf" -a -f "${package}/LICENSE" -a -f "${package}/README.md"

install_options=(--no-start --autostart)
if [[ "${system_mode}" != "--system" ]]; then
    install_options+=(--no-system)
fi
"${package}/install.sh" ${install_options[@]+"${install_options[@]}"}

binary="${HOME}/.local/bin/ultragear-input"
check "installs the binary" test -x "${binary}"
check "binary runs" "${binary}" --version
check "lists displays as JSON" bash -c "'${binary}' --list-displays | python3 -c 'import json, sys; assert isinstance(json.load(sys.stdin), list)'"
check "adds the launcher" grep -q "Exec=\"${binary}\"" "${XDG_DATA_HOME}/applications/ultragear-input.desktop"
check "enables Start at Login" cmp -s "${XDG_DATA_HOME}/applications/ultragear-input.desktop" "${XDG_CONFIG_HOME}/autostart/ultragear-input.desktop"
if command -v desktop-file-validate > /dev/null; then
    check "launcher passes desktop-file-validate" desktop-file-validate "${XDG_DATA_HOME}/applications/ultragear-input.desktop"
fi

if [[ "${system_mode}" == "--system" ]]; then
    check "installs the udev rule" cmp -s "${package}/60-ultragear-input.rules" /etc/udev/rules.d/60-ultragear-input.rules
    check "loads i2c-dev at boot" cmp -s "${package}/ultragear-input.conf" /etc/modules-load.d/ultragear-input.conf
    check "provides the i2c-dev interface" i2c_dev_available
    if udevadm verify --help > /dev/null 2>&1; then
        check "udev accepts the rule" udevadm verify /etc/udev/rules.d/60-ultragear-input.rules
    fi
fi

mkdir -p "${XDG_CONFIG_HOME}/ultragear-input"
echo '{}' > "${XDG_CONFIG_HOME}/ultragear-input/settings.json"
uninstall_options=()
if [[ "${system_mode}" == "--system" ]]; then
    uninstall_options+=(--system)
fi
"${package}/uninstall.sh" ${uninstall_options[@]+"${uninstall_options[@]}"}
check "removes the binary" test ! -e "${binary}"
check "removes the launcher" test ! -e "${XDG_DATA_HOME}/applications/ultragear-input.desktop"
check "removes Start at Login" test ! -e "${XDG_CONFIG_HOME}/autostart/ultragear-input.desktop"
check "keeps settings without --purge" test -f "${XDG_CONFIG_HOME}/ultragear-input/settings.json"
if [[ "${system_mode}" == "--system" ]]; then
    check "removes the udev rule" test ! -e /etc/udev/rules.d/60-ultragear-input.rules
    check "removes the module setting" test ! -e /etc/modules-load.d/ultragear-input.conf
fi
"${package}/uninstall.sh" --purge
check "purge removes settings" test ! -e "${XDG_CONFIG_HOME}/ultragear-input"

echo
if ((failures > 0)); then
    echo "${failures} installer checks failed"
    exit 1
fi
echo "All installer checks passed"
