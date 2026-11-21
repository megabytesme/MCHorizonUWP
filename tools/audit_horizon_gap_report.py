"""Enumerate each non-verified cell in the settings, renderer and shader matrices."""

import csv
import io
import re
from collections import Counter
from pathlib import Path

from audit_horizon_evidence import PROFILES, STATUS


OUTPUT = Path("deliverables/feature-audit")
LEDGER = "verification-gaps.csv"
PROFILE_TESTS = {
    "x64-d3d12": ("x64", "D3D12", "D3D11 FL not applicable", "Start the signed x64 QA game using Dx12 on the RTX 3090 Ti."),
    "arm64-d3d12": ("ARM64", "D3D12", "D3D11 FL not applicable", "Start the staged signed ARM64 game using Dx12 on Surface Pro X. Confirm the interactive session and exact installed client version."),
    "x86-d3d12": ("x86", "D3D12", "D3D11 FL not applicable", "Start the signed x86 game using Dx12. Require an in-world frame beyond the existing launcher probe."),
    "lumia-arm32-d3d12": ("ARM32", "D3D12", "D3D11 FL not applicable", "On Lumia, first reproduce hardware D3D12 adapter rejection. Reopen this setting test only if a hardware game route becomes available."),
    "x64-d3d11": ("x64", "D3D11", "FL11_1 hardware, FL9_1 compatibility", "Start the signed x64 game using Dx11 without force. Record both device negotiation and compatibility projection."),
    "x86-d3d11": ("x86", "D3D11", "FL11_1 hardware, FL9_1 compatibility", "Start the signed x86 game using Dx11 without force. Record both device negotiation and compatibility projection."),
    "arm64-d3d11": ("ARM64", "D3D11", "FL11_1 hardware, FL9_1 compatibility", "Start the signed ARM64 game using Dx11 on Surface Pro X. Record the active client, adapter and projection."),
    "lumia-arm32-d3d11": ("ARM32", "D3D11", "FL11_1 hardware, FL9_1 compatibility", "Use the isolated signed ARM32 app on Lumia. Record negotiation, projection and exact package."),
    "desktop-d3d11-fl91": ("x64", "D3D11", "Actual forced FL9_1", "Use the x64 force-FL9_1 QA route. Require an actual FL9_1 device and prove the in-world effect independently of the scaler probe."),
    "x86-desktop-d3d11-fl91": ("x86", "D3D11", "Actual forced FL9_1", "Use the x86 force-FL9_1 QA route. Require an actual FL9_1 device and prove the in-world effect independently of the scaler probe."),
    "arm64-surface-d3d11-fl91": ("ARM64", "D3D11", "Actual forced FL9_1", "Use the ARM64 force-FL9_1 QA route on Surface Pro X. Require an actual FL9_1 device and an in-world assertion."),
    "lumia-arm32-d3d11-fl91": ("ARM32", "D3D11", "Actual forced FL9_1", "Use the isolated ARM32 force-FL9_1 QA package on Lumia. Require an actual FL9_1 device and an in-world assertion."),
    "x64-angle": ("x64", "ANGLE", "GLES 3.0 over D3D11. Driver D3D11 FL unrecorded.", "Start the signed x64 game using ANGLE. Record GLES limits and actual rendering and fallback behavior."),
    "x86-angle": ("x86", "ANGLE", "Underlying D3D11 FL unrecorded", "First reproduce the x86 EGL surface-creation failure with the exact package. Require EGL config and surface proof before in-world A/B."),
    "arm64-angle": ("ARM64", "ANGLE", "Underlying D3D11 FL unrecorded", "Start the signed ARM64 game using ANGLE on Surface Pro X. Require in-world presentation beyond the earlier native probe."),
    "lumia-arm32-angle": ("ARM32", "ANGLE", "Underlying D3D11 FL unrecorded", "First reproduce the ARM32 EGL surface failure through the paired Lumia Portal. Require a working window surface before in-world A/B."),
}
SHADER_PROFILES = {
    "uwp_dx12_x64": "x64-d3d12",
    "uwp_dx12_arm64": "arm64-d3d12",
    "uwp_dx11_compat_x64": "x64-d3d11",
    "uwp_dx11_compat_x86": "x86-d3d11",
    "uwp_dx11_compat_arm64": "arm64-d3d11",
    "uwp_dx11_forced_actual_fl91_desktop": "desktop-d3d11-fl91",
    "uwp_dx11_lumia_arm32": "lumia-arm32-d3d11",
    "uwp_angle_x64_gles30": "x64-angle",
}
OFFICIAL = ("Windows", "Official Windows renderer", "Compare the exact official Windows source/release.")
FIELDS = (
    "matrix", "id", "profile", "architecture", "backend", "feature_level", "status",
    "classification", "repair_owner", "observed_or_blocker", "source_and_evidence",
    "reproduction", "acceptance",
)
CLASSES = {
    "B": "broken",
    "S": "stub",
    "U": "missing implementation",
    "H": "intentionally capability-gated",
    "X": "driver, hardware or OS limited",
    "P": "not tested in full",
    "C": "not tested",
    "?": "not tested",
}
OFFICIAL_LIMITS = {"H", "S", "U", "B", "X"}
SHARED_SHADER_LIMITS = {
    "classic.geometry": "P",
    "classic.tessellation": "U",
    "native.bt2020": "U",
    "native.entity": "P",
    "native.motion": "U",
}


def profile_details(profile, official=False):
    if profile in PROFILE_TESTS:
        architecture, backend, feature_level, setup = PROFILE_TESTS[profile]
        if official:
            return architecture, backend, "D3D11 FL not assigned to the stock baseline", OFFICIAL[2]
        return architecture, backend, feature_level, setup
    if profile in SHADER_PROFILES:
        return PROFILE_TESTS[SHADER_PROFILES[profile]]
    if profile.startswith("uwp_dx11_modern_"):
        architecture = profile.rsplit("_", 1)[1]
        return (architecture, "D3D11", "Modern renderer unavailable at the pinned product",
                "Check the compatibility dispatch first. A modern-path runtime test requires a candidate exposing that path.")
    if profile == "uwp_angle_arm":
        return "ARM (matrix aggregate)", "ANGLE", "Underlying D3D11 FL unrecorded", PROFILE_TESTS["arm64-angle"][3]
    architecture = profile.rsplit("_", 1)[1] if profile.startswith("official_windows_dx12_") else OFFICIAL[0]
    return architecture, "D3D12" if profile.startswith("official_windows_dx12_") else OFFICIAL[1], "D3D11 FL not applicable", OFFICIAL[2]


def as_text(value):
    if isinstance(value, (tuple, list)):
        return " | ".join(str(item) for item in value)
    return str(value) if value is not None else ""


def classify(status, official, matrix, identifier):
    if status not in STATUS:
        raise ValueError(f"Unknown matrix status {status!r}")
    upstream_limit = official and status in OFFICIAL_LIMITS
    shared_shader_limit = matrix == "shader" and SHARED_SHADER_LIMITS.get(identifier) == status
    if upstream_limit or shared_shader_limit:
        return "official upstream limitation"
    return CLASSES[status]


def gap(matrix, identifier, profile, status, owner, finding, refs, repro, acceptance, official=False):
    architecture, backend, feature_level, setup = profile_details(profile, official)
    return {
        "matrix": matrix, "id": identifier,
        "profile": f"official/{profile}" if official else profile,
        "architecture": architecture, "backend": backend, "feature_level": feature_level,
        "status": status, "classification": classify(status, official, matrix, identifier), "repair_owner": owner,
        "observed_or_blocker": finding,
        "source_and_evidence": refs,
        "reproduction": " ".join((setup, repro)).strip(),
        "acceptance": acceptance,
    }


def inventory_gaps(rows):
    for row in rows:
        identifier = row["id"]
        owner = row["owner"]
        refs = as_text(row.get("sources"))
        effect = row.get("expected_visible_effect") or row.get("official_windows_behavior")
        effect = as_text(effect) or "Record the actual behavior or confirm the absence of a live effect."
        acceptance = row.get("test_method") or row.get("recommendation") or effect
        test = f"Exercise {row['key']} in the scoped fixture. Confirm {effect[0].lower()}{effect[1:]}"
        matrix = "feature" if row["scope"] == "feature" else "setting"
        if row["scope"] != "feature" and row.get("storage"):
            if row["scope"] in {"cli", "developer"}:
                test = f"Compare omitted and exercised {row['key']} using {row['storage']}. {acceptance}"
            elif row.get("uwp_ui"):
                test = (f"Use a clean profile and change {row['key']} in {row['storage']}. "
                        "Compare the default and an alternate value in the same scene. "
                        "Reopen the profile and repeat the effect check.")
        base = row["official_baseline_status"]
        if base != "V":
            yield gap(matrix, identifier, "windows", base, "Horizon",
                      row.get("official_windows_behavior") or "Official behavior still needs a scoped receipt.",
                      " | ".join(filter(None, [refs, as_text(row.get("official_windows_evidence"))])),
                      test, acceptance, official=True)
        for profile, value in row["profiles"].items():
            code = value["status"]
            if code != "V":
                source = as_text(value.get("evidence")) or refs
                yield gap(matrix, identifier, profile, code, owner, value["condition"],
                          source, test, acceptance)


def renderer_gaps(files):
    path = OUTPUT / "renderer-features.csv"
    with (files / path).open(encoding="utf-8-sig", newline="") as stream:
        for line, row in enumerate(csv.DictReader(stream), 2):
            ref = f"{path.as_posix()}:{line}"
            profile = row["profile_id"]
            identifier = row["feature_id"]
            proof = row["proof_gap"] or row["enabling_conditions"]
            for official, column in ((False, "status"), (True, "official_windows_status")):
                code = row[column]
                if code == "V":
                    continue
                finding = row["official_windows_behavior"] if official else row["port_behavior"]
                evidence = row.get("official_windows_evidence") if official else row["evidence"]
                refs = " | ".join(filter(None, (ref, row["source_refs"], evidence)))
                yield gap("renderer", identifier, profile, code, "Horizon" if official else row["owner"],
                          finding, refs, row["enabling_conditions"], proof, official)


def shader_owner(test_id, profile, status):
    if profile.startswith("official_windows"):
        return "Horizon shader engine"
    if "dx11" in profile and status == "H":
        return "MCHorizonUWP / D3D11 tier owner"
    if "angle" in profile and status in {"B", "X", "S", "H"}:
        return "Horizon shader engine / ANGLE"
    return "Horizon shader engine / UWP QA"


def shader_gaps(files):
    path = OUTPUT / "shader-pack-tests.csv"
    with (files / path).open(encoding="utf-8-sig", newline="") as stream:
        for line, row in enumerate(csv.DictReader(stream), 2):
            for name, code in row.items():
                if not re.match(r"^(?:uwp_|official_windows_)", name) or code == "V":
                    continue
                ref = f"{path.as_posix()}:{line} | {row['source_anchor']}"
                diagnosis = row.get("limiting_layer") or row["observation"]
                finding = f"{diagnosis} {row['observation']}" if diagnosis != row["observation"] else diagnosis
                repro = row.get("reproduction") or f"Use fixture {row['fixture']} and exercise {row['capability']}."
                yield gap("shader", row["test_id"], name, code, shader_owner(row["test_id"], name, code),
                          finding, ref, repro, row["required_proof"], name.startswith("official_windows"))


def compile_ledger(settings, features, root):
    """Return the generated CSV and summary counts for deterministic audit output."""
    if set(PROFILE_TESTS) != set(PROFILES):
        raise ValueError("Verification setup must cover every pinned profile.")
    rows = [*inventory_gaps(settings + features), *renderer_gaps(root), *shader_gaps(root)]
    ids = {(row["matrix"], row["id"], row["profile"]) for row in rows}
    if len(ids) != len(rows):
        raise ValueError("Duplicate verification gap ID and profile.")
    output = io.StringIO()
    writer = csv.DictWriter(output, fieldnames=FIELDS, lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    counts = {
        "ledger": LEDGER,
        "unverified_or_defective_cells": len(rows),
        "by_matrix": dict(sorted(Counter(row["matrix"] for row in rows).items())),
        "by_status": dict(sorted(Counter(row["status"] for row in rows).items())),
        "by_classification": dict(sorted(Counter(row["classification"] for row in rows).items())),
    }
    return output.getvalue(), counts
