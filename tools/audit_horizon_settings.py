#!/usr/bin/env python3
"""Generate the Horizon Windows / UWP audit from pinned source trees."""

import argparse
import csv
import hashlib
import io
import json
import re
import subprocess
from collections import Counter
from pathlib import Path

import audit_horizon_extras
import audit_horizon_launcher
import audit_horizon_render_settings
import audit_horizon_world
import audit_horizon_vanilla
from audit_horizon_core import graphics, setting_row
from audit_horizon_evidence import (
    ACTIVE, DX11, FEATURE_OVERRIDES, OFFICIAL_HDR, OFFICIAL_HDR_AUTO,
    OWNERS, PIN, PRODUCT, PROFILES, SHADER, STATUS, WGPU,
    apply_cells, audit_setting, cell,
)
from audit_horizon_surfaces import bundled_pack_settings, dynamic_controls, parse_preset_tables, registered_settings


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = Path("deliverables/feature-audit")
PARITY = Path("deliverables/nightly-2026-11-20/feature-parity.json")
PATCH = "patches/nightly-2026-11-20/horizon-uwp-0.1.15.patch"
GAPS = {
    "launcher-settings": "Pinned settings storage, Settings card and CLI declarations are indexed. Nested launcher dialogs, other pages, and detailed equivalence to Builder and the UWP shell need a fuller crosswalk.",
    "create-world-settings": "Stock create-world controls and declared game rules are listed. Datapack-defined choices, separate dedicated server configuration and service settings require additional coverage.",
    "runtime-registered-controls": "The finite source inventory covers control types and shipped native packs; installed mods, classic packs and hardware-dependent choices supply additional values at runtime.",
    "settings-metadata": "Unresolved field-level behavior, exact ranges and controls are listed by record ID.",
    "hardware-matrix": "Physical acceptance requires the exact signed version, negotiated device/profile, active route and exercised values.",
}
OLD_PROFILES = {
    "x64_dx11_fl91": "x64-d3d11", "x86_dx11_fl91": "x86-d3d11",
    "arm64_dx11_fl91": "arm64-d3d11", "arm32_dx11_fl91": "lumia-arm32-d3d11",
    "x64_dx12": "x64-d3d12", "x86_dx12": "x86-d3d12",
    "arm64_dx12": "arm64-d3d12", "arm32_dx12": "lumia-arm32-d3d12",
    "x64_angle": "x64-angle", "x86_angle": "x86-angle", "arm32_angle": "lumia-arm32-angle",
}
BACKLOG = (
    ("P0", "HDR", "HDR Auto, On and calibration",
     "Capture Windows HDR state, actual SwapChainPanel DXGI format/color space and measured black, white and peak nits. Retest SDR fallback, resize, resume and monitor transitions.", "graphics.hdr"),
    ("P0", "wgpu", "D3D11 feature-level certification and renderer dispatch",
     "Certify 9_1 through 11_1 in order against native/API obligations. Record requested and returned tiers, signed world tests, active route and masked features.", "feature.renderer-dx11"),
    ("P0", "MCHorizonUWP", "Shader pack execution on signed profiles",
     "Capture named pack, digest, stage-specific frame A/B and output status on each admitted backend. Exercise invalid reload, options and combinations.", "graphics.pickedShaderPack"),
    ("P0", "MCHorizonUWP", "Surface 0.1.17 timing and adaptive scale",
     "Run the staged signed Surface package with repeatable load. Validate timestamps and fallback, world dimensions and HUD under resize/restore.", "graphics.dynamicResolution"),
    ("P1", "ANGLE", "Backend admission and presentation",
     "Trace x86 and Lumia EGL surface failures independently. Verify EGL configs, GLES limits and pack stages on each architecture.", "feature.renderer-angle"),
    ("P1", "MCHorizonUWP", "Input, accessibility, audio and lifecycle",
     "Exercise F2, IME, clipboard, device routing, narrator, controller, touch, gyro orientations and true AppContainer suspend/resume.", "feature.accessibility"),
    ("P1", "MCHorizonUWP", "Network identity and persistence",
     "Use separate test account and world for secure sign-in/refresh, online-mode join, LAN and save/restart.", "feature.online-account"),
    ("P2", "Horizon", "Individual settings and source coverage",
     "Reconcile launcher, world creation and late-bound settings. Resolve missing metadata and add signed A/B evidence with controls.", "feature.desktop-launcher"),
)


def git(root, *arguments):
    command = ["git", "-C", str(root), *arguments]
    return subprocess.run(command, check=True, text=True, capture_output=True,
                          encoding="utf-8").stdout.strip()


def feature_rows(product):
    previous = json.loads((product / PARITY).read_text(encoding="utf-8"))
    source = (product / PARITY).read_text(encoding="utf-8").splitlines()
    id_lines = {match[1]: number for number, line in enumerate(source, 1)
                if (match := re.match(r'^\s+"id": "([^"]+)",?$', line))}
    rows = []
    for old in previous["features"]:
        identifier = old["id"]
        refs = [f"product:{PARITY.as_posix()}:{id_lines[identifier]}"]
        row = setting_row("feature", identifier, None, None, None, refs,
                          old["official_behavior"], persisted=None)
        row.update(
            scope="feature", category=old["category"],
            uwp_ui="See the grouped facility and individual settings records.",
            uwp_runtime_action=old["uwp_behavior_and_root_cause"],
            recommendation=old["feasible_follow_up_and_acceptance"],
            test_method=old["feasible_follow_up_and_acceptance"],
            owner="MCHorizonUWP", evidence=old["evidence_refs"], historical=True,
        )
        for profile, status in old["baseline_status_by_profile"].items():
            if status != "X":
                row["profiles"][OLD_PROFILES[profile]] = cell(
                    "C", f"Historical matrix status {status}; broader or different signed baseline.",
                    [PARITY.as_posix()],
                )
        if identifier in FEATURE_OVERRIDES:
            profiles, status, condition, evidence = FEATURE_OVERRIDES[identifier]
            apply_cells(row, profiles, status, condition, evidence)
            row["uwp_runtime_action"] = condition
            row["evidence"] = sorted(set(row["evidence"] + evidence))
            row["historical"] = False
            if status in {"U", "S"}:
                row["root_cause"] = condition
        if identifier in {"hdr", "renderer-dx12"}:
            row["official_baseline_status"] = "P"
            row["official_windows_evidence"] = [OFFICIAL_HDR]
            row["official_windows_behavior"] = OFFICIAL_HDR_AUTO
        if identifier in {"shader-pack-native", "shader-pack-classic"}:
            classic = identifier.endswith("classic")
            apply_cells(row, DX11, "H", "The current compatibility renderer bypasses shader-pack execution.")
            log = SHADER + ("gl-classic/game.log" if classic else "gl-golden/game.log")
            behavior = ("Classic terrain-test hit missing VERTEX_STORAGE and exited." if classic
                        else "Golden Hour sky pipeline layout failed with fallback world rendering.")
            apply_cells(row, ("x64-angle",), "B", behavior, [log])
        rows.append(row)
    return rows, {f"product:{PARITY.as_posix()}": [row["key"] for row in rows]}


def merge_coverage(coverage, extra):
    for path, declarations in extra.items():
        coverage.setdefault(path, []).extend(declarations)


def finalize(row):
    status = row.pop("uwp_uniform_status", None)
    if status:
        condition = row.get("root_cause") or row.get("uwp_runtime_action") or "See cited UWP source."
        for profile in ACTIVE:
            row["profiles"].setdefault(profile, cell(status, condition, row["sources"]))
    profiles = row["profiles"]
    unknown = sorted(set(profiles).difference(PROFILES))
    if unknown:
        raise ValueError(f"{row['id']}: unknown profiles {unknown}")
    for profile in PROFILES:
        existing = profiles.get(profile)
        if isinstance(existing, str):
            profiles[profile] = cell(existing, "See row evidence and conditions.", row["sources"])
        elif existing is None:
            code = "C" if profile in ACTIVE and not row.get("historical") else "?"
            profiles[profile] = cell(code, "No matching setting-specific physical receipt.")
    if any(value["status"] not in STATUS for value in profiles.values()):
        raise ValueError(f"{row['id']}: unrecognized status")
    if any(value["status"] == "V" and not value["evidence"] for value in profiles.values()):
        raise ValueError(f"{row['id']}: V requires evidence")
    row["profiles"] = {profile: profiles[profile] for profile in PROFILES}
    row.setdefault("owner", "Horizon")
    row.setdefault("evidence", [])
    row.setdefault("official_baseline_status", "C")
    return row


def inventory(official, port, product=ROOT):
    coverage = {}
    settings = []
    for extract in (graphics, audit_horizon_extras.inventory, audit_horizon_vanilla.inventory,
                    audit_horizon_world.inventory, audit_horizon_launcher.inventory,
                    audit_horizon_render_settings.inventory,
                    dynamic_controls, registered_settings):
        rows, declarations = extract(official, port)
        settings.extend(rows)
        merge_coverage(coverage, declarations)
    parse_preset_tables(official, settings, coverage)
    settings.extend(bundled_pack_settings(official, coverage))
    for row in settings:
        row.setdefault("profiles", {})
        if isinstance(row.get("evidence"), str):
            row["evidence"] = [row["evidence"]]
        else:
            row.setdefault("evidence", [])
        if "uwp_uniform_status" not in row:
            audit_setting(row, port)
    features, declarations = feature_rows(product)
    merge_coverage(coverage, declarations)
    merge_coverage(coverage, audit_horizon_render_settings.link_renderer(settings, features, product))
    settings = sorted((finalize(row) for row in settings), key=lambda row: row["id"])
    features = sorted((finalize(row) for row in features), key=lambda row: row["id"])
    duplicates = [identifier for identifier, count in Counter(
        row["id"] for row in settings + features).items() if count > 1]
    if duplicates:
        raise ValueError(f"Duplicate row IDs: {duplicates}")
    return settings, features, coverage


def structured(value):
    return json.dumps(value, ensure_ascii=False, indent=2) + "\n"


def csv_value(value):
    if isinstance(value, (dict, list, tuple, bool)):
        return json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    return value


def settings_csv(rows):
    fields = ("id", "key", "scope", "aliases", "official_ui_label", "official_ui_translation_key", "official_ui_page", "storage",
              "values", "default", "official_baseline_status", "official_windows_behavior", "official_windows_evidence",
              "expected_visible_effect", "uwp_ui", "uwp_persistence", "uwp_runtime_action",
              "enabling_conditions", "sources", "test_method", "evidence", "root_cause",
              "recommendation", "owner")
    out = io.StringIO()
    writer = csv.writer(out, lineterminator="\n")
    writer.writerow([*fields, *PROFILES])
    for row in rows:
        writer.writerow([*(csv_value(row.get(key)) for key in fields),
                         *(row["profiles"][profile]["status"] for profile in PROFILES)])
    return out.getvalue()


def coverage_report(settings, features, declarations):
    per_file = {}
    for path, items in sorted(declarations.items()):
        names = sorted(set(items))
        per_file[path] = {"count": len(names), "declarations": names}
    requirements = ("official_ui_label", "official_ui_page", "values", "default",
                    "official_windows_behavior", "expected_visible_effect", "uwp_runtime_action")
    missing = {}
    non_setting_scopes = {"action", "cli", "debug-preset", "feature"}
    for field in requirements:
        ids = [row["id"] for row in settings if row.get(field) in (None, "")
               and not (field == "default" and row["scope"] in non_setting_scopes)
               and not (field in {"official_ui_label", "official_ui_page"}
                        and row["scope"] in {"config", "hidden", "developer", "cli", "legacy migration"})]
        if ids:
            missing[field] = ids
    return {
        "settings_and_actions": len(settings),
        "feature_groups": len(features),
        "scopes": dict(sorted(Counter(row["scope"] for row in settings).items())),
        "profiles": len(PROFILES),
        "setting_profile_status_counts": dict(sorted(Counter(
            value["status"] for row in settings for value in row["profiles"].values()).items())),
        "source_files": per_file,
        "metadata_requires_review": missing,
        "exhaustive_certified": False,
        "known_scope_gaps": GAPS,
    }


def escape(value):
    return str(value).replace("|", r"\|").replace("\n", " ")


def unsupported_markdown(rows):
    lines = [
        "# Horizon UWP limitations and proof gaps", "",
        "Each row below has a broken, gated, missing, no-op, or demonstrated hardware/OS limitation.",
        "The JSON inventory gives profile scope, source anchors and individual test methods.", "",
        "| Owner | ID | Affected profiles | Evidence finding |",
        "| --- | --- | --- | --- |",
    ]
    for row in sorted(rows, key=lambda value: (value.get("owner", ""), value["id"])):
        affected = [(name, value) for name, value in row["profiles"].items()
                    if value["status"] in {"B", "S", "H", "U", "X"}]
        if affected:
            profiles = ", ".join(f"{name} ({value['status']})" for name, value in affected)
            explanation = row.get("root_cause") or ". ".join(dict.fromkeys(
                value["condition"].rstrip(".") for _, value in affected))
            lines.append(f"| {row['owner']} | `{row['id']}` | {profiles} | {escape(explanation)} |")
    lines += ["", "## Prioritized backlog", "",
              "| Priority | Owning layer | Work item | Acceptance evidence | Related ID |",
              "| --- | --- | --- | --- | --- |"]
    lines += [f"| {priority} | {owner} | {item} | {acceptance} | `{identifier}` |"
              for priority, owner, item, acceptance, identifier in BACKLOG]
    lines += ["", "## Repository and layer map", ""]
    lines += [f"- **{owner}** covers {description}." for owner, description in OWNERS.items()]
    return "\n".join(lines) + "\n"


def translate_minecraft_labels(settings, language):
    if language is None:
        return None, {}
    data = language.read_bytes()
    labels = json.loads(data)
    prefix = "prepared:assets/minecraft/lang/en_us.json"
    used = set()
    for row in settings:
        key = row["official_ui_label"]
        if isinstance(key, str) and key in labels:
            row["official_ui_translation_key"] = key
            row["official_ui_label"] = labels[key]
            used.add(key)
    return hashlib.sha256(data).hexdigest(), {prefix: sorted(used)}

def generate(official, port, product, language=None):
    settings, features, coverage = inventory(official, port, product)
    label_digest, label_coverage = translate_minecraft_labels(settings, language)
    merge_coverage(coverage, label_coverage)
    model = {
        "schema_version": 1,
        "provenance": {"official": PIN, "uwp_product": PRODUCT, "wgpu": WGPU,
                       "uwp_patch": PATCH, "client": "0.1.17", "builder": "1.0.9",
                       "prepared_minecraft_en_us_sha256": label_digest},
        "statuses": STATUS,
        "profiles": PROFILES,
        "source_paths": {
            "official": "Pinned Horizon source",
            "uwp": "Horizon source with the pinned 0.1.15 product patch applied",
            "product": "MCHorizonUWP product pin",
            "wgpu": "Pinned wgpu fork",
        },
        "settings": settings,
        "user_visible_feature_groups": features,
    }
    return {
        "settings-inventory.json": structured(model),
        "settings-matrix.csv": settings_csv(settings + features),
        "coverage.json": structured(coverage_report(settings, features, coverage)),
        "unsupported-features.md": unsupported_markdown(settings + features),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--official", type=Path, required=True)
    parser.add_argument("--uwp-source", type=Path, required=True)
    parser.add_argument("--minecraft-language", type=Path,
                        help="Pinned prepared Minecraft assets/minecraft/lang/en_us.json")
    parser.add_argument("--output", type=Path, default=ROOT / OUTPUT)
    parser.add_argument("--check", action="store_true", help="Compare outputs without writing")
    options = parser.parse_args()
    if git(options.official, "rev-parse", "HEAD") != PIN or git(options.uwp_source, "rev-parse", "HEAD") != PIN:
        parser.error(f"Both sources must be based on official Horizon {PIN}.")
    if git(options.official, "status", "--porcelain", "--untracked-files=no"):
        parser.error("Official tracked source must match the pin.")
    try:
        git(options.uwp_source, "apply", "--reverse", "--check", str(ROOT / PATCH))
    except subprocess.CalledProcessError as error:
        parser.error("UWP source must have the 0.1.15 product patch applied. " + error.stderr.strip())
    products = generate(options.official, options.uwp_source, ROOT, options.minecraft_language)
    if options.check:
        differences = [name for name, content in products.items()
                       if not (options.output / name).is_file()
                       or (options.output / name).read_text(encoding="utf-8") != content]
        if differences:
            parser.exit(1, "Out of date: " + ", ".join(differences) + "\n")
    else:
        options.output.mkdir(parents=True, exist_ok=True)
        for name, content in products.items():
            (options.output / name).write_bytes(content.encode("utf-8"))
    coverage = json.loads(products["coverage.json"])
    print(f"{coverage['settings_and_actions']} settings/actions, {coverage['feature_groups']} feature groups, "
          f"{len(coverage['source_files'])} source files, {coverage['profiles']} profiles")


if __name__ == "__main__":
    main()
