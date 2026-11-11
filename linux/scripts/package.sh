#!/usr/bin/env bash
set -euo pipefail

linux_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository_dir="$(cd "${linux_dir}/.." && pwd)"
version="$(tr -d '[:space:]' < "${repository_dir}/VERSION")"
output_dir="${linux_dir}/dist"
project="${repository_dir}/dotnet/src/UltraGearInput.Linux/UltraGearInput.Linux.csproj"
if (($# > 0)); then
    architectures=("$@")
else
    architectures=(x64 arm64)
fi

if tar --version 2> /dev/null | grep -q GNU; then
    ownership=(--owner=0 --group=0 --numeric-owner)
else
    ownership=(--uid 0 --gid 0 --numeric-owner)
fi

checksum() {
    if command -v sha256sum > /dev/null; then
        sha256sum "$1"
    else
        shasum -a 256 "$1"
    fi
}

rm -rf "${output_dir}"
mkdir -p "${output_dir}"

for architecture in "${architectures[@]}"; do
    name="ultragear-input-${version}-linux-${architecture}"
    stage="${output_dir}/${name}"
    dotnet publish "${project}" --configuration Release --runtime "linux-${architecture}" --output "${output_dir}/publish-${architecture}"
    mkdir -p "${stage}"
    install -m 755 "${output_dir}/publish-${architecture}/ultragear-input" "${stage}/ultragear-input"
    install -m 755 "${linux_dir}/packaging/install.sh" "${linux_dir}/packaging/uninstall.sh" "${stage}/"
    install -m 644 "${linux_dir}/packaging/60-ultragear-input.rules" "${linux_dir}/packaging/ultragear-input.conf" "${stage}/"
    install -m 644 "${repository_dir}/LICENSE" "${repository_dir}/THIRD_PARTY_NOTICES.md" "${stage}/"
    install -m 644 "${repository_dir}/docs/linux.md" "${stage}/README.md"
    tar -C "${output_dir}" "${ownership[@]}" -czf "${output_dir}/${name}.tar.gz" "${name}"
    (cd "${output_dir}" && checksum "${name}.tar.gz" > "${name}.tar.gz.sha256")
    rm -rf "${stage}" "${output_dir}/publish-${architecture}"
    echo "${output_dir}/${name}.tar.gz"
done
