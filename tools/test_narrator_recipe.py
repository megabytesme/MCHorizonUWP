"""Checks for the isolated November 20 Narrator release recipe."""
import tempfile
import unittest
from pathlib import Path

import prepare_candidate as candidate


class NarratorRecipeTests(unittest.TestCase):
    def test_pins_distinct_client_and_hdr_inputs(self):
        patch = candidate.NARRATOR_PATCH
        self.assertEqual(candidate.NIGHTLY_RECIPES[patch], ("0.1.19", "1.0.10"))
        self.assertIn(patch, candidate.NOV20_PATCHES)
        self.assertIn(patch, candidate.HDR_PATCHES)
        self.assertIn(patch, candidate.FIXED_WGPU_PATCH_VERSIONS)
        self.assertEqual(candidate.PATCH_PATHS[patch],
                         "patches/nightly-2026-11-20/horizon-uwp-0.1.19.patch")
        self.assertEqual(candidate.NIGHTLY_RECIPES[candidate.GRAPHICS_COMPATIBILITY_PATCH],
                         ("0.1.18", "1.0.10"))

    def test_host_kit_must_expose_narrator_on_every_architecture(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            data = b"\0".join(candidate.NARRATOR_EXPORTS)
            for arch in ("arm", "arm64", "x86", "x64"):
                (root / arch).mkdir()
                for name in ("HorizonUWP.exe", "HorizonUWPHost.lib"):
                    (root / arch / name).write_bytes(data)
            candidate.verify_narrator_host_kit(root)
            (root / "arm" / "HorizonUWPHost.lib").write_bytes(b"old host")
            with self.assertRaisesRegex(ValueError, "missing narrator exports: arm/HorizonUWPHost.lib"):
                candidate.verify_narrator_host_kit(root)
            (root / "arm" / "HorizonUWP.exe").unlink()
            with self.assertRaisesRegex(ValueError, "incomplete: arm/HorizonUWP.exe"):
                candidate.verify_narrator_host_kit(root)


if __name__ == "__main__":
    unittest.main()
