#!/usr/bin/env bash
set -euo pipefail

mkdir -p /run/dbus
dbus-daemon --system --fork
exec setpriv --reuid=tester --regid=tester --init-groups -- python3 /repository/linux/tests/integration.py "$@"
