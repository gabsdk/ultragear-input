#!/usr/bin/env bash

i2c_dev_available() {
    test -d "${1:-/sys}/class/i2c-dev"
}

ensure_i2c_stub() {
    if ! modinfo i2c-stub > /dev/null 2>&1; then
        sudo apt-get update
        sudo apt-get install -y "linux-modules-extra-$(uname -r)"
    fi
    sudo modprobe i2c-dev
    sudo modprobe i2c-stub chip_addr=0x37
}
