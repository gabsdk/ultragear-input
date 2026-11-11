#!/usr/bin/env bash
set -euo pipefail

tests_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${tests_dir}/kernel.sh"

workspace="$(mktemp -d)"
trap 'rm -rf "${workspace}"' EXIT

expect() {
    local expected="$1"
    local actual="$2"
    if [[ "${expected}" != "${actual}" ]]; then
        printf 'FAIL expected <%s>, got <%s>\n' "${expected}" "${actual}" >&2
        exit 1
    fi
}

if i2c_dev_available "${workspace}"; then
    echo "FAIL accepts an absent i2c-dev interface" >&2
    exit 1
fi
mkdir -p "${workspace}/class/i2c-dev"
i2c_dev_available "${workspace}"
echo "PASS detects built-in i2c-dev without a module directory"

modinfo() { return "${module_status}"; }
uname() { echo "test-kernel"; }
sudo() {
    printf '%s\n' "$*" >> "${workspace}/calls"
    if [[ "$*" == "modprobe i2c-stub chip_addr=0x37" ]]; then
        return "${load_status}"
    fi
}

module_status=0
load_status=0
ensure_i2c_stub
expect $'modprobe i2c-dev\nmodprobe i2c-stub chip_addr=0x37' "$(cat "${workspace}/calls")"
echo "PASS loads the existing stub without installing kernel packages"

module_status=1
: > "${workspace}/calls"
ensure_i2c_stub
expect $'apt-get update\napt-get install -y linux-modules-extra-test-kernel\nmodprobe i2c-dev\nmodprobe i2c-stub chip_addr=0x37' "$(cat "${workspace}/calls")"
echo "PASS installs matching modules when the stub is missing"

module_status=0
load_status=1
set +e
(set -e; ensure_i2c_stub)
status=$?
set -e
expect 1 "${status}"
echo "PASS propagates a stub load failure"
