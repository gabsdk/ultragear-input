import hashlib
import os
import re
import subprocess
import sys
from pathlib import Path


def release_tag(version):
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version):
        raise ValueError("VERSION must contain a stable semantic version")
    return "v" + version


def release_assets(directory, version):
    names = [
        f"UltraGearInput-macos-{version}.zip",
        f"UltraGearInput-windows-x64-{version}.exe",
        f"ultragear-input-{version}-linux-x64.tar.gz",
        f"ultragear-input-{version}-linux-arm64.tar.gz",
    ]
    expected = sorted(names + [name + ".sha256" for name in names])
    if sorted(path.name for path in directory.iterdir()) != expected:
        raise ValueError("Release requires exactly four packages and their checksums")
    for name in names:
        package = directory / name
        checksum = directory / (name + ".sha256")
        if package.is_symlink() or checksum.is_symlink() or package.stat().st_size == 0:
            raise ValueError("Release packages must be nonempty regular files")
        digest = hashlib.sha256()
        with package.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        if checksum.read_text().strip() != f"{digest.hexdigest()}  {name}":
            raise ValueError("Package checksum does not match: " + name)
    return [str(directory / name) for name in expected]


def publish(directory):
    if os.environ["GITHUB_REF"] != "refs/heads/main":
        raise ValueError("Releases require main")
    version = Path("VERSION").read_text().strip()
    tag = release_tag(version)
    assets = release_assets(directory, version)
    sha = os.environ["GITHUB_SHA"]
    repository = os.environ["GITHUB_REPOSITORY"]
    subprocess.run(["git", "merge-base", "--is-ancestor", sha, "origin/main"], check=True)
    existing = subprocess.run(["git", "rev-parse", "--verify", f"refs/tags/{tag}^{{commit}}"], capture_output=True, text=True)
    if existing.returncode == 0 and existing.stdout.strip() != sha:
        raise ValueError("VERSION already belongs to another commit; increment VERSION")
    release = subprocess.run(["gh", "release", "view", tag, "--repo", repository], capture_output=True)
    if release.returncode != 0:
        subprocess.run(["gh", "release", "create", tag, "--repo", repository, "--target", sha,
                        "--title", "UltraGear Input " + version, "--generate-notes", "--draft"], check=True)
    subprocess.run(["gh", "release", "upload", tag, *assets, "--repo", repository, "--clobber"], check=True)
    subprocess.run(["gh", "release", "edit", tag, "--repo", repository, "--draft=false", "--prerelease=false", "--latest"], check=True)


if __name__ == "__main__":
    publish(Path(sys.argv[1]))
