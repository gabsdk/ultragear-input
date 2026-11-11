#!/usr/bin/env bash
set -euo pipefail

package_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository_dir="$(cd "${package_dir}/.." && pwd)"
version="$(tr -d '[:space:]' < "${repository_dir}/VERSION")"
output_dir="${package_dir}/dist"
app_dir="${output_dir}/UltraGear Input.app"
archive="${output_dir}/UltraGearInput-macos-${version}.zip"

swift build --package-path "${package_dir}" --configuration release --arch arm64
binary_dir="$(swift build --package-path "${package_dir}" --configuration release --arch arm64 --show-bin-path)"

rm -rf "${output_dir}"
mkdir -p "${app_dir}/Contents/MacOS" "${app_dir}/Contents/Resources"
cp "${binary_dir}/UltraGearInput" "${app_dir}/Contents/MacOS/UltraGearInput"
cp -R "${binary_dir}/UltraGearInput_UltraGearInputCore.bundle" "${app_dir}/Contents/Resources/"
sed "s/\$(VERSION)/${version}/g" "${package_dir}/Resources/Info.plist" > "${app_dir}/Contents/Info.plist"
plutil -lint "${app_dir}/Contents/Info.plist" > /dev/null

codesign --force --options runtime --timestamp=none --sign "${CODESIGN_IDENTITY:--}" "${app_dir}"
codesign --verify --strict "${app_dir}"

ditto -c -k --norsrc --keepParent "${app_dir}" "${archive}"
shasum -a 256 "${archive}" | sed "s|${output_dir}/||" > "${archive}.sha256"
echo "${archive}"
