#!/usr/bin/env python3
"""Prepare local, ARM32-specific fixes for generated windows-sys bindings.

The checked-in worktree and the Cargo registry are not modified. The output
is an ignored local directory containing patched copies and a Cargo config.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / ".local" / "arm32-windows-sys"
CARGO_HOME = Path(os.environ.get("CARGO_HOME", Path.home() / ".cargo"))
VERSIONS = ("0.52.0", "0.59.0", "0.60.2", "0.61.2")
XXHASH_VERSION = "0.8.19"
FILES = {
    "src/Windows/Win32/Networking/WinSock/mod.rs": ("WSADATA", "SERVENT"),
    "src/Windows/Win32/System/WindowsProgramming/mod.rs": ("DELAYLOAD_INFO",),
    "src/Windows/Win32/System/Memory/mod.rs": ("MEMORY_BASIC_INFORMATION",),
}
EXPECTED_COUNTS = {"0.52.0": 3, "0.59.0": 1, "0.60.2": 2, "0.61.2": 2}


def find_source(version: str) -> Path | None:
    candidates = sorted((CARGO_HOME / "registry" / "src").glob(f"*/windows-sys-{version}"))
    return candidates[0] if candidates else None


def fetch_missing(offline: bool) -> None:
    missing = [version for version in VERSIONS if find_source(version) is None]
    missing_ureq = offline and not list((CARGO_HOME / "registry" / "src").glob("*/ureq-2.12.1"))
    missing_xxhash = offline and not list((CARGO_HOME / "registry" / "src").glob(f"*/xxhash-rust-{XXHASH_VERSION}"))
    if not missing and not missing_ureq and not missing_xxhash:
        return
    # A scratch manifest lets Cargo download verified crates without changing
    # the game workspace's lockfile or installed tools.
    scratch = OUTPUT / "fetch"
    scratch.mkdir(parents=True, exist_ok=True)
    manifest = '[package]\nname = "horizon-arm32-dep-fetch"\nversion = "0.0.0"\nedition = "2021"\n[workspace]\n[dependencies]\n'
    for version in VERSIONS:
        alias = "windows-sys-" + version.replace(".", "-")
        manifest += f'{alias} = {{ package = "windows-sys", version = "={version}" }}\n'
    if offline:
        manifest += 'ureq = { version = "=2.12.1", default-features = false }\n'
        manifest += f'xxhash-rust = {{ version = "={XXHASH_VERSION}", features = ["xxh3"] }}\n'
    (scratch / "Cargo.toml").write_text(manifest, encoding="utf-8")
    (scratch / "src").mkdir(exist_ok=True)
    (scratch / "src" / "lib.rs").write_text("", encoding="utf-8")
    subprocess.run(["cargo", "fetch", "--manifest-path", str(scratch / "Cargo.toml")], check=True)


def patch_file(source: Path, target: Path, version: str, names: tuple[str, ...]) -> None:
    original = source.read_text(encoding="utf-8")
    result = original
    for name in names:
        pattern = (
            r'#\[cfg\(target_arch = "x86"\)\]'
            rf'(?=\n(?:#\[[^\n]*\]\n)*(?:pub struct {name}\b|impl[^\n]*\bfor {name}\b))'
        )
        result, count = re.subn(pattern, '#[cfg(any(target_arch = "x86", target_arch = "arm"))]', result)
        if count != EXPECTED_COUNTS[version]:
            raise RuntimeError(f"Unexpected {name} declarations in {source}: {count}")
    current = target.read_text(encoding="utf-8") if target.exists() else None
    if current not in (None, original, result):
        raise RuntimeError(f"Refusing to overwrite unexpected local changes in {target}")
    if current != result:
        target.write_text(result, encoding="utf-8", newline="\n")


def prepare_offline_ureq() -> str:
    """Make an explicitly opted-in ARM32-only build that rejects HTTPS."""
    candidates = sorted((CARGO_HOME / "registry" / "src").glob("*/ureq-2.12.1"))
    if not candidates:
        raise RuntimeError("ureq 2.12.1 is not cached; run cargo fetch for the game bridge first")
    source = candidates[0]
    target = OUTPUT / "ureq-2.12.1-offline"
    if not target.exists():
        shutil.copytree(source, target)
    manifest = target / "Cargo.toml"
    original = (source / "Cargo.toml").read_text(encoding="utf-8")
    patched, count = re.subn(r'tls = \[\n(?:[^\n]*\n)*?\]', 'tls = []', original, count=1)
    if count != 1:
        raise RuntimeError("Unexpected ureq TLS feature declaration")
    current = manifest.read_text(encoding="utf-8")
    if current not in (original, patched):
        raise RuntimeError(f"Refusing to overwrite unexpected local changes in {manifest}")
    manifest.write_text(patched, encoding="utf-8", newline="\n")
    for relative in ("src/lib.rs", "src/agent.rs", "src/test/range.rs"):
        input_file = source / relative
        output_file = target / relative
        original = input_file.read_text(encoding="utf-8")
        patched = original.replace('feature = "tls"', 'all(feature = "tls", not(all(target_arch = "arm", target_vendor = "uwp")))')
        if relative == "src/lib.rs":
            patched += '\n#[cfg(not(all(target_arch = "arm", target_vendor = "uwp")))]\ncompile_error!("The offline ureq override is only for experimental ARM32 UWP builds");\n'
        if patched == original:
            raise RuntimeError(f"Missing expected TLS feature gate in {input_file}")
        current = output_file.read_text(encoding="utf-8")
        if current not in (original, patched):
            raise RuntimeError(f"Refusing to overwrite unexpected local changes in {output_file}")
        output_file.write_text(patched, encoding="utf-8", newline="\n")
    return f'ureq = {{ path = {json.dumps(target.as_posix())} }}'


def prepare_arm32_xxhash() -> str:
    """Use xxhash-rust's portable path on ARM32, where its NEON intrinsics are unstable."""
    candidates = sorted((CARGO_HOME / "registry" / "src").glob(f"*/xxhash-rust-{XXHASH_VERSION}"))
    if not candidates:
        raise RuntimeError(f"xxhash-rust {XXHASH_VERSION} is not cached; run Cargo fetch first")
    source = candidates[0]
    target = OUTPUT / f"xxhash-rust-{XXHASH_VERSION}-arm32"
    if not target.exists():
        shutil.copytree(source, target)
    source_manifest = (source / "Cargo.toml").read_text(encoding="utf-8")
    manifest = target / "Cargo.toml"
    current_manifest = manifest.read_text(encoding="utf-8")
    if current_manifest != source_manifest:
        raise RuntimeError("The local xxhash-rust manifest differs from the verified registry source")
    source_file = source / "src" / "xxh3.rs"
    target_file = target / "src" / "xxh3.rs"
    original = source_file.read_text(encoding="utf-8")
    patched = original.replace(
        'target_feature = "neon"',
        'all(target_feature = "neon", not(target_arch = "arm"))',
    )
    if patched == original:
        raise RuntimeError("xxhash-rust has no NEON feature gates to prepare for ARM32")
    current = target_file.read_text(encoding="utf-8")
    if current not in (original, patched):
        raise RuntimeError(f"Refusing to overwrite unexpected local changes in {target_file}")
    if current != patched:
        target_file.write_text(patched, encoding="utf-8", newline="\n")
    return f'xxhash-rust = {{ path = {json.dumps(target.as_posix())} }}'


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--offline-arm32", action="store_true", help="Create a separate ARM32-only experimental override that rejects HTTPS")
    args = parser.parse_args()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    fetch_missing(args.offline_arm32)
    lines = ["[patch.crates-io]"]
    for version in VERSIONS:
        source = find_source(version)
        if source is None:
            raise RuntimeError(f"Cargo did not download windows-sys {version}")
        target = OUTPUT / f"windows-sys-{version}"
        if not target.exists():
            shutil.copytree(source, target)
        for relative, names in FILES.items():
            patch_file(source / relative, target / relative, version, names)
        alias = "windows-sys-" + version.replace(".", "-")
        lines.append(f'{alias} = {{ package = "windows-sys", path = {json.dumps(target.as_posix())} }}')
    config = OUTPUT / "patches.toml"
    config.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Prepared ARM32 windows-sys copies. Pass Cargo --config {config}")
    if args.offline_arm32:
        offline_config = OUTPUT / "patches-offline-arm32.toml"
        offline_config.write_text("\n".join(lines + [prepare_offline_ureq(), prepare_arm32_xxhash()]) + "\n", encoding="utf-8")
        print(f"EXPERIMENTAL: HTTPS will fail; use only for ARM32 UWP. Pass Cargo --config {offline_config}")


if __name__ == "__main__":
    main()
