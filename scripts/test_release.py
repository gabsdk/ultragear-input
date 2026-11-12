import hashlib
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import release


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        for name in ["UltraGearInput-macos-1.2.3.zip", "UltraGearInput-windows-x64-1.2.3.exe",
                     "ultragear-input-1.2.3-linux-x64.tar.gz", "ultragear-input-1.2.3-linux-arm64.tar.gz"]:
            data = name.encode()
            (self.directory / name).write_bytes(data)
            (self.directory / (name + ".sha256")).write_text(hashlib.sha256(data).hexdigest() + "  " + name + "\n")

    def test_requires_a_stable_version(self):
        self.assertEqual("v1.2.3", release.release_tag("1.2.3"))
        for version in ["1.2", "01.2.3", "1.2.3-beta", "v1.2.3", "1.2.3\n"]:
            with self.subTest(version=version), self.assertRaises(ValueError):
                release.release_tag(version)

    def test_requires_all_packages_and_checksums(self):
        self.assertEqual(8, len(release.release_assets(self.directory, "1.2.3")))
        next(self.directory.glob("*.sha256")).unlink()
        with self.assertRaises(ValueError):
            release.release_assets(self.directory, "1.2.3")

    def test_rejects_unexpected_artifacts(self):
        (self.directory / "screenshot.png").write_bytes(b"image")
        with self.assertRaises(ValueError):
            release.release_assets(self.directory, "1.2.3")

    def test_rejects_a_changed_package(self):
        next(self.directory.glob("*.exe")).write_bytes(b"changed")
        with self.assertRaises(ValueError):
            release.release_assets(self.directory, "1.2.3")

    def test_rejects_development_without_publishing(self):
        with patch.dict(os.environ, GITHUB_REF="refs/heads/development"), patch("release.subprocess.run") as run:
            with self.assertRaises(ValueError):
                release.publish(self.directory)
            run.assert_not_called()

    def test_publishes_main_and_targets_the_verified_commit(self):
        environment = {"GITHUB_REF": "refs/heads/main", "GITHUB_SHA": "a" * 40, "GITHUB_REPOSITORY": "owner/repo"}
        with patch.dict(os.environ, environment), patch("release.Path.read_text", return_value="1.2.3"), \
                patch("release.release_assets", return_value=["verified-package"]), patch("release.subprocess.run") as run:
            run.side_effect = [subprocess.CompletedProcess([], 0), subprocess.CompletedProcess([], 1, ""),
                               subprocess.CompletedProcess([], 1), subprocess.CompletedProcess([], 0),
                               subprocess.CompletedProcess([], 0), subprocess.CompletedProcess([], 0)]
            release.publish(self.directory)
            create = run.call_args_list[3].args[0]
            self.assertEqual(["gh", "release", "create", "v1.2.3"], create[:4])
            self.assertEqual(environment["GITHUB_SHA"], create[create.index("--target") + 1])
            self.assertIn("verified-package", run.call_args_list[4].args[0])
            self.assertIn("--draft", create)
            self.assertIn("--draft=false", run.call_args_list[5].args[0])

    def test_does_not_publish_when_upload_fails(self):
        environment = {"GITHUB_REF": "refs/heads/main", "GITHUB_SHA": "a" * 40, "GITHUB_REPOSITORY": "owner/repo"}
        with patch.dict(os.environ, environment), patch("release.Path.read_text", return_value="1.2.3"), \
                patch("release.release_assets", return_value=[]), patch("release.subprocess.run") as run:
            run.side_effect = [subprocess.CompletedProcess([], 0), subprocess.CompletedProcess([], 1, ""),
                               subprocess.CompletedProcess([], 1), subprocess.CompletedProcess([], 0),
                               subprocess.CalledProcessError(1, ["gh", "release", "upload"])]
            with self.assertRaises(subprocess.CalledProcessError):
                release.publish(self.directory)
            self.assertEqual(5, run.call_count)

    def test_never_moves_a_tag_from_another_commit(self):
        environment = {"GITHUB_REF": "refs/heads/main", "GITHUB_SHA": "a" * 40, "GITHUB_REPOSITORY": "owner/repo"}
        with patch.dict(os.environ, environment), patch("release.Path.read_text", return_value="1.2.3"), \
                patch("release.release_assets", return_value=[]), patch("release.subprocess.run") as run:
            run.side_effect = [subprocess.CompletedProcess([], 0), subprocess.CompletedProcess([], 0, "b" * 40)]
            with self.assertRaises(ValueError):
                release.publish(self.directory)
            self.assertEqual(2, run.call_count)

    def test_never_reuses_a_draft_from_another_commit(self):
        environment = {"GITHUB_REF": "refs/heads/main", "GITHUB_SHA": "a" * 40, "GITHUB_REPOSITORY": "owner/repo"}
        with patch.dict(os.environ, environment), patch("release.Path.read_text", return_value="1.2.3"), \
                patch("release.release_assets", return_value=[]), patch("release.subprocess.run") as run:
            run.side_effect = [subprocess.CompletedProcess([], 0), subprocess.CompletedProcess([], 1, ""),
                               subprocess.CompletedProcess([], 0, '{"targetCommitish":"' + "b" * 40 + '"}')]
            with self.assertRaises(ValueError):
                release.publish(self.directory)
            self.assertEqual(3, run.call_count)


if __name__ == "__main__":
    unittest.main()
