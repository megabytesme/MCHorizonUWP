import hashlib
import json
from pathlib import Path
import runpy
import tempfile
import tomllib
import unittest


PROBE = Path(__file__).resolve().parent
STAGER = runpy.run_path(str(PROBE / "stage-packaged.py"))
GAME = "mchorizonuwp-0.1.8-patch-0.1.7-x64-0b14f8acfd97fa2c750e42795249dcc981178ca4"
FIXTURE = PROBE / "hook-source"


class PackagedSourceTests(unittest.TestCase):
    def setUp(self):
        root = tempfile.TemporaryDirectory(prefix="horizon-native-packaged-test-")
        self.addCleanup(root.cleanup)
        self.workspace = Path(root.name)
        self.bridge = self.workspace / "uwp/game-bridge"
        self.bridge.mkdir(parents=True)
        (self.bridge / "Cargo.toml").write_text("[workspace]\n\n[package]\nname = 'bridge'\nversion = '0.1.0'\n")
        (self.bridge / "Cargo.lock").write_text(
            'version = 4\n\n[[package]]\nname = "bridge"\nversion = "0.1.0"\n'
        )
        self.output = self.bridge / "packaged-native"

    def test_exact_native_source_snapshot_and_target_record(self):
        STAGER["stage"](self.workspace, "x64", [FIXTURE], GAME)
        record = json.loads((self.output / "build-record.json").read_bytes())
        self.assertEqual(record["rust_target"], "x86_64-uwp-windows-msvc")
        row, = record["mods"]
        self.assertEqual(row["identity"], f"{GAME} uwp_native_hook_probe 0.1.0 1.99.1")
        self.assertEqual(row["dll"], "horizon_uwp_mod_uwp_native_hook_probe.dll")
        for relative in ("Cargo.toml", "horizon-mod.toml", "src/lib.rs"):
            original = (FIXTURE / relative).read_bytes()
            packaged = (self.output / "horizon-native-mods" / row["id"] / relative).read_bytes()
            self.assertEqual(packaged, original)
            self.assertEqual(row["source_sha256"][relative], hashlib.sha256(original).hexdigest())
        lock = (self.bridge / "Cargo.lock").read_bytes()
        self.assertEqual(record["cargo_lock_sha256"], hashlib.sha256(lock).hexdigest())
        self.assertIn(row["cargo_package"], {pkg["name"] for pkg in tomllib.loads(lock.decode())["package"]})

    def test_wrong_architecture_and_duplicate_mods_leave_workspace_unchanged(self):
        for architecture, sources, message in [
            ("arm64", [FIXTURE], "pinned UWP game build identity"),
            ("x64", [FIXTURE, FIXTURE], "Duplicate native mod ID"),
        ]:
            with self.subTest(architecture=architecture, count=len(sources)):
                with self.assertRaisesRegex(ValueError, message):
                    STAGER["stage"](self.workspace, architecture, sources, GAME)
                self.assertFalse(self.output.exists())

    def test_two_native_sources_preserve_manifests_in_sorted_package_inventory(self):
        dependent = PROBE / "hook-after-source"
        STAGER["stage"](self.workspace, "x64", [FIXTURE, dependent], GAME)
        record = json.loads((self.output / "build-record.json").read_bytes())
        self.assertEqual([mod["id"] for mod in record["mods"]],
                         ["uwp_native_after_probe", "uwp_native_hook_probe"])
        path = self.output / "horizon-native-mods/uwp_native_after_probe/horizon-mod.toml"
        self.assertEqual(tomllib.loads(path.read_text(encoding="utf-8"))["dependencies"],
                         {"uwp_native_hook_probe": "=0.1.0"})


if __name__ == "__main__":
    unittest.main()
