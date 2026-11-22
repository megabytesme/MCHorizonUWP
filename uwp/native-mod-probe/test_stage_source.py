import importlib.util
from pathlib import Path
import shutil
import tempfile
import unittest


PROBE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("stage_source", PROBE / "stage-source.py")
STAGER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(STAGER)


class NativeSourceTests(unittest.TestCase):
    def setUp(self):
        root = tempfile.TemporaryDirectory(prefix="horizon-native-source-test-")
        self.addCleanup(root.cleanup)
        self.mod = Path(root.name) / "mod"
        shutil.copytree(PROBE / "hook-source", self.mod)

    def test_accepts_standard_hook_sources(self):
        package, manifest, files = STAGER.read_source(self.mod)
        self.assertEqual(package["edition"], "2024")
        self.assertEqual(manifest["id"], "uwp_native_hook_probe")
        self.assertEqual(files["src/lib.rs"], (self.mod / "src/lib.rs").read_bytes())

    def test_preserves_horizon_manifest_version_independently_of_cargo(self):
        path = self.mod / "horizon-mod.toml"
        path.write_text(path.read_text(encoding="utf-8").replace(
            'version = "0.1.0"', 'version = "1.0"'), encoding="utf-8")
        package, manifest, _ = STAGER.read_source(self.mod)
        self.assertEqual(package["version"], "0.1.0")
        self.assertEqual(manifest["version"], "1.0")

    def test_requires_the_pinned_manifest_capability(self):
        path = self.mod / "horizon-mod.toml"
        path.write_text(path.read_text(encoding="utf-8").replace('["registries"]', "[]"),
                        encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "standard registries declaration"):
            STAGER.read_source(self.mod)

    def test_rejects_user_build_scripts_and_additional_dependencies(self):
        build_script = self.mod / "build.rs"
        build_script.write_text('fn main() { panic!("must never run"); }', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "custom build files"):
            STAGER.read_source(self.mod)
        build_script.unlink()
        with (self.mod / "Cargo.toml").open("a", encoding="utf-8") as cargo:
            cargo.write('external-proc-macro = "1"\n')
        with self.assertRaisesRegex(ValueError, "other dependencies"):
            STAGER.read_source(self.mod)

    def test_detects_other_nightly_and_wrong_target_identity(self):
        path = self.mod / "horizon-mod.toml"
        text = path.read_text(encoding="utf-8").replace(STAGER.UPSTREAM, "a" * 40)
        path.write_text(text, encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "exact Nightly"):
            STAGER.read_source(self.mod)
        workspace = self.mod.parent / "empty-workspace"
        workspace.mkdir()
        with self.assertRaisesRegex(ValueError, "build identity|Build identity"):
            STAGER.stage(workspace, "arm64", [self.mod],
                         f"mchorizonuwp-0.1.7-patch-0.1.6-x64-{STAGER.UPSTREAM}")
        self.assertFalse(any(workspace.iterdir()))


if __name__ == "__main__":
    unittest.main()
