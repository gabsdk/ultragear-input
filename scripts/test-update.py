#!/usr/bin/env python3
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(binary, app):
    with tempfile.TemporaryDirectory(prefix="ultragear-update-test-") as temporary:
        root = Path(temporary)
        settings = root / "home" / "settings.json"
        settings.parent.mkdir()
        settings.write_text('{"keep":true}')
        environment = dict(os.environ, HOME=str(settings.parent), XDG_CONFIG_HOME=str(settings.parent / "config"))
        for scenario in ("checksum", "version", "valid"):
            corrupt = scenario != "valid"
            stage = root / scenario
            stage.mkdir(mode=0o700)
            helper = stage / ("helper.exe" if os.name == "nt" else "helper")
            shutil.copy2(binary, helper)
            if app:
                target = root / (scenario + " App.app")
                shutil.copytree(app, target)
                marker = target / "previous-version-marker"
                marker.write_text("previous")
                payload = stage / "extracted" / "UltraGear Input.app"
                shutil.copytree(app, payload)
                bundle_name = "UltraGearInput_UltraGearInputCore.bundle"
                shutil.copytree(app / "Contents" / "Resources" / bundle_name, stage / bundle_name)
                target_binary = target / "Contents" / "MacOS" / "UltraGearInput"
                payload_binary = payload / "Contents" / "MacOS" / "UltraGearInput"
                version = subprocess.check_output(["/usr/libexec/PlistBuddy", "-c", "Print CFBundleShortVersionString", str(app / "Contents/Info.plist")], text=True).strip()
            else:
                target = root / ("previous app.exe" if os.name == "nt" else "previous app")
                shutil.copy2(binary, target)
                with target.open("ab") as output:
                    output.write(b"previous-version-marker")
                payload = stage / "payload"
                shutil.copy2(binary, payload)
                target_binary = target
                payload_binary = payload
                version = subprocess.check_output([str(binary), "--version"], text=True, env=environment).strip()
                marker = None
            previous = digest(target_binary)
            expected = digest(payload_binary)
            plan = {
                "target": str(target),
                "payload": str(payload),
                "sha256": "0" * 64 if scenario == "checksum" else expected,
                "parentId": 2147483647,
                "parentStarted": 0,
                "version": "0.0.1" if scenario == "version" else version,
                "restart": False,
            }
            plan_file = stage / "plan.json"
            plan_file.write_text(json.dumps(plan))
            completed = subprocess.run([str(helper), "--apply-update", str(plan_file)], env=environment, capture_output=True, text=True, timeout=45)
            if corrupt:
                assert completed.returncode != 0, completed.stdout + completed.stderr
                assert digest(target_binary) == previous
                if marker:
                    assert marker.exists()
                print(f"PASS {scenario} failure keeps the installed app", flush=True)
            else:
                assert completed.returncode == 0, completed.stdout + completed.stderr
                assert digest(target_binary) == expected
                if marker:
                    assert not marker.exists()
                print("PASS packaged helper replaces the isolated installed app", flush=True)
            assert settings.read_text() == '{"keep":true}'
            print("PASS update preserves settings", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--app", type=Path)
    arguments = parser.parse_args()
    run(arguments.binary.resolve(), arguments.app.resolve() if arguments.app else None)
