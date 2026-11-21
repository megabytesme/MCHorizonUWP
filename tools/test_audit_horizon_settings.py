"""Cross-check the settings audit against the pinned source declarations."""

import csv
import io
import json
import os
import re
import tomllib
import unittest
from pathlib import Path

import audit_horizon_settings as audit
from audit_horizon_core import RENDER, read
from audit_horizon_evidence import PROFILES, STATUS


OFFICIAL = os.environ.get("HORIZON_AUDIT_OFFICIAL")
UWP = os.environ.get("HORIZON_AUDIT_UWP_SOURCE")


def parse_case_labels(text):
    return set(re.findall(r'(?m)^\s{16}"([a-zA-Z][a-zA-Z0-9]+)"(?: if .+)? =>', text))


class AuditSchemaTests(unittest.TestCase):
    def test_verified_cell_requires_evidence(self):
        input_row = {"id": "fixture", "sources": [], "profiles":
                     {"x64-d3d12": {"status": "V", "evidence": []}}}
        with self.assertRaisesRegex(ValueError, "V requires evidence"):
            audit.finalize(input_row)

    def test_language_resources_resolve_only_exact_keys(self):
        from tempfile import TemporaryDirectory

        with TemporaryDirectory() as directory:
            language = Path(directory) / "en_us.json"
            language.write_text('{"options.fov":"FOV"}', encoding="utf-8")
            settings = [{"official_ui_label": "options.fov"}, {"official_ui_label": "Missing"}]
            digest, coverage = audit.translate_minecraft_labels(settings, language)
        self.assertEqual(len(digest), 64)
        self.assertEqual(settings[0], {"official_ui_label": "FOV", "official_ui_translation_key": "options.fov"})
        self.assertEqual(settings[1]["official_ui_label"], "Missing")
        self.assertEqual(next(iter(coverage.values())), ["options.fov"])

    def test_offline_fixture_receipt_covers_each_native_fixture(self):
        fixture_root = audit.ROOT / "tools/feature-audit-fixtures/native"
        receipt_path = audit.ROOT / audit.OUTPUT / "shader-evidence/followup-offline.json"
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        captured = {entry["fixture"]: entry for entry in receipt["native"]}
        actual = {manifest.parent.name for manifest in fixture_root.glob("*/horizon-shaders.toml")}
        self.assertEqual(set(captured), actual)
        self.assertFalse(receipt["runtime_verified"])
        for name, item in captured.items():
            if name == "audit_invalid":
                self.assertTrue(item["exit_code_nonzero"])
                self.assertIn("post.wgsl:2:12", item["diagnostic"])
            else:
                self.assertRegex(item["sha256"], r"^[a-f0-9]{64}$")

    def test_official_hdr_receipt_is_the_pinned_windows_run(self):
        from audit_horizon_evidence import OFFICIAL_HDR, PIN

        receipt = json.loads((audit.ROOT / OFFICIAL_HDR).read_text(encoding="utf-8"))
        self.assertEqual(receipt["source_commit"], PIN)
        self.assertEqual(receipt["request"]["hdr"], "auto")
        self.assertTrue(receipt["monitor"]["system_hdr_active"])
        self.assertEqual(receipt["renderer"]["backend"], "Dx12")
        self.assertTrue(any("HDR output presented its first frame" in line
                            for line in receipt["log_evidence"]))

    def test_duplicate_profile_is_rejected(self):
        input_row = {"id": "fixture", "sources": [], "profiles": {"invented": "C"}}
        with self.assertRaisesRegex(ValueError, "unknown profiles"):
            audit.finalize(input_row)


@unittest.skipUnless(OFFICIAL and UWP,
                     "Set HORIZON_AUDIT_OFFICIAL and HORIZON_AUDIT_UWP_SOURCE for source checks.")
class PinnedSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.official = Path(OFFICIAL)
        cls.port = Path(UWP)
        cls.settings, cls.features, cls.coverage = audit.inventory(cls.official, cls.port)
        cls.by_id = {row["id"]: row for row in cls.settings}

    def test_graphics_parser_serializer_and_ui(self):
        source = read(self.official, RENDER / "graphics.rs")
        key_table = source.split("match key.trim() {", 1)[1].split("_ => {}", 1)[0]
        parsed = parse_case_labels(key_table)
        serial = source.split("pub fn serialize(&self)", 1)[1].split("pub fn save(", 1)[0]
        serialized = set(re.findall(r'\\n([a-zA-Z][a-zA-Z0-9]+):', serial))
        self.assertTrue(serialized.issubset(parsed))
        keys = {row["key"] for row in self.settings if row["storage"] == "horizon-graphics.txt"}
        self.assertEqual(parsed, keys)
        covered = self.coverage[f"official:{RENDER.as_posix()}/graphics.rs"]
        self.assertEqual({key for key in covered if not key.startswith("HORIZON_")}, parsed)
        ui = read(self.official, RENDER / "graphics_ui.rs")
        public_enum = ui.split("pub enum H {", 1)[1].split("\n}", 1)[0]
        declarations = set(re.findall(r'(?m)^    (\w+),$', public_enum))
        recorded = self.coverage[f"official:{RENDER.as_posix()}/graphics_ui.rs"]
        self.assertEqual(declarations, {name.split("::", 1)[1] for name in recorded if name.startswith("H::")})

    def test_extras_and_pvp_parser_keys(self):
        extras = read(self.official, RENDER / "extras.rs")
        table = extras.split("match key.trim() {", 1)[1].split("e.pvp.parse_line", 1)[0]
        keys = parse_case_labels(table) | {"noAi", "threeDSkinLayers", "emoteKey"}
        self.assertEqual(keys, {r["key"] for r in self.settings if r["id"].startswith("extras.")
                                and not r["id"].startswith(("extras.pvp.", "extras.cosmetics."))})
        pvp = read(self.official, RENDER / "pvp.rs")
        table = pvp.split("pub fn parse_line", 1)[1].split("_ => return false", 1)[0]
        keys = set(re.findall(r'(?m)^ {12}"(\w+)"(?: if .+?)? =>', table))
        self.assertEqual(keys, {r["key"] for r in self.settings if r["id"].startswith("extras.pvp.")
                                and r["storage"] == "horizon-extras.txt"})

    def test_bundled_shader_options(self):
        for path in (self.official / "crates/horizon-shader-api/packs").glob("*/horizon-shaders.toml"):
            options = tomllib.loads(path.read_text(encoding="utf-8")).get("options", [])
            prefix = f"shader.pack.{path.parent.name}."
            self.assertEqual({f"{prefix}{option['id']}" for option in options},
                             {name for name in self.by_id if name.startswith(prefix)})

    def test_sources_exist_and_cover_each_row(self):
        source_roots = {"official": self.official, "uwp": self.port, "product": audit.ROOT}
        files = {}
        for row in self.settings + self.features:
            with self.subTest(row=row["id"]):
                self.assertTrue(row["sources"])
                self.assertEqual(set(PROFILES), set(row["profiles"]))
                self.assertIn(row["official_baseline_status"], STATUS)
                for source in row["sources"]:
                    kind, _, address = source.partition(":")
                    if kind not in source_roots:
                        continue
                    file, _, line = address.rpartition(":")
                    key = kind, file
                    if key not in files:
                        files[key] = len((source_roots[kind] / file).read_text(encoding="utf-8").splitlines())
                    self.assertGreaterEqual(int(line), 1, source)
                    self.assertLessEqual(int(line), files[key], source)

    def test_declared_options_and_world_rules_have_rows(self):
        from audit_horizon_vanilla import options_defs
        from audit_horizon_world import rule_defs

        base_options = set(options_defs(self.official))
        actual_options = {row["key"] for row in self.settings if row["storage"] == "options.txt"}
        self.assertLessEqual(base_options, actual_options)
        self.assertTrue(base_options)
        rules = set(rule_defs(self.official))
        actual_rules = {row["key"] for row in self.settings if row["storage"] == "data/minecraft/game_rules.dat"}
        self.assertEqual(rules, actual_rules)

    def test_output_is_deterministic_and_does_not_lose_rows(self):
        first = audit.generate(self.official, self.port, audit.ROOT)
        second = audit.generate(self.official, self.port, audit.ROOT)
        self.assertEqual(first, second)
        csv_rows = list(csv.DictReader(io.StringIO(first["settings-matrix.csv"])))
        self.assertEqual([row["id"] for row in self.settings + self.features],
                         [row["id"] for row in csv_rows])
        gap_csv = list(csv.DictReader(io.StringIO(first["verification-gaps.csv"])))
        got = {(row["matrix"], row["id"], row["profile"], row["status"]) for row in gap_csv}
        self.assertEqual(len(got), len(gap_csv))
        expected = {
            ("feature" if row["scope"] == "feature" else "setting", row["id"], profile, status)
            for row in self.settings + self.features
            for profile, status in [
                ("official/windows", row["official_baseline_status"]),
                *((profile, cell["status"]) for profile, cell in row["profiles"].items()),
            ]
            if status != "V"
        }
        with (audit.ROOT / audit.OUTPUT / "renderer-features.csv").open(encoding="utf-8-sig", newline="") as source:
            for row in csv.DictReader(source):
                for prefix, field in (("", "status"), ("official/", "official_windows_status")):
                    if row[field] != "V":
                        expected.add(("renderer", row["feature_id"], prefix + row["profile_id"], row[field]))
        with (audit.ROOT / audit.OUTPUT / "shader-pack-tests.csv").open(encoding="utf-8-sig", newline="") as source:
            for row in csv.DictReader(source):
                for key, value in row.items():
                    if key.startswith(("official_windows_", "uwp_")) and value != "V":
                        profile = "official/" + key if key.startswith("official_windows_") else key
                        expected.add(("shader", row["test_id"], profile, value))
        self.assertEqual(expected, got)
        for row in gap_csv:
            self.assertTrue(row["classification"] and row["repair_owner"] and row["reproduction"])
        declared = json.loads(first["coverage.json"])
        for path, members in self.coverage.items():
            self.assertEqual(declared["source_files"][path]["count"], len(set(members)))


if __name__ == "__main__":
    unittest.main()
