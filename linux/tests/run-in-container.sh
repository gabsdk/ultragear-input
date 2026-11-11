#!/usr/bin/env bash
set -euo pipefail

tests_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_dir="$(cd "${tests_dir}/../.." && pwd)"
binary="${1:?Usage: run-in-container.sh <linux binary> [screenshot dir] [i2c-stub bus]}"
screenshots="${2:-${repository_dir}/linux/tests/screenshots}"
stub_bus="${3:-}"
image="ultragear-input-linux-test"
extra=()
if [[ -n "${stub_bus}" ]]; then
    extra=(--device "/dev/i2c-${stub_bus}")
    arguments=(--stub-bus "${stub_bus}")
else
    arguments=()
fi

mkdir -p "${screenshots}"
chmod 777 "${screenshots}"
docker build --quiet --tag "${image}" "${tests_dir}" > /dev/null
docker run --rm \
    --volume "${repository_dir}:/repository:ro" \
    --volume "$(cd "$(dirname "${binary}")" && pwd)/$(basename "${binary}"):/opt/ultragear-input:ro" \
    --volume "${screenshots}:/screenshots" \
    ${extra[@]+"${extra[@]}"} \
    "${image}" --binary /opt/ultragear-input --screenshots /screenshots ${arguments[@]+"${arguments[@]}"}
