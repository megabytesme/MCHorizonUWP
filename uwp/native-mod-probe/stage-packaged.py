"""Prepare trusted Horizon hook crates for separate UWP modpack DLL builds."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import runpy
import tomllib


SCRIPTS = Path(__file__).resolve().parent
STAGING = runpy.run_path(str(SCRIPTS / "stage-source.py"))
TARGETS = {name: target for name, target in STAGING["TARGETS"].items() if name != "arm"}
TOOLCHAIN = "1.99.1"


def stage(workspace, architecture, sources, game_build_id):
    expected = (r"mchorizonuwp-[0-9]+\.[0-9]+\.[0-9]+-patch-[0-9]+\.[0-9]+\.[0-9]+-"
                + architecture + "-" + STAGING["UPSTREAM"])
    november20 = f"mchorizonuwp-0.1.14-patch-0.1.13-{architecture}-{STAGING['NOV20_UPSTREAM']}"
    if not re.fullmatch(expected, game_build_id) and game_build_id != november20:
        raise ValueError("Expected the pinned UWP game build identity for this architecture")
    bridge = workspace / "uwp/game-bridge"
    output = bridge / "packaged-native"
    if output.exists():
        raise ValueError("Packaged native sources are already staged in this assembly")
    upstream = game_build_id.rsplit("-", 1)[1]
    checked = [STAGING["read_source"](source, upstream) for source in sources]
    ids = {manifest["id"] for _, manifest, _ in checked}
    if len(ids) != len(checked):
        raise ValueError("Duplicate native mod ID")

    lock_path = bridge / "Cargo.lock"
    lock = lock_path.read_text(encoding="utf-8")
    known = {item["name"] for item in tomllib.loads(lock)["package"]}
    cargo_path = bridge / "Cargo.toml"
    cargo = cargo_path.read_text(encoding="utf-8")
    added, members, mods = [], [], []

    for package, manifest, files in sorted(checked, key=lambda item: item[1]["id"]):
        mod_id, version = manifest["id"], manifest["version"]
        if version != version.strip() or len(version.split()) != 1:
            raise ValueError(f"{mod_id}: the native build identity requires a single version token")
        name = "horizon-uwp-native-" + mod_id.replace("_", "-")
        if name in known:
            raise ValueError(f"{mod_id}: generated Cargo package name is already in the lockfile")
        identity = f"{game_build_id} {mod_id} {version} {TOOLCHAIN}"
        library = f"horizon_uwp_mod_{mod_id}"
        member = f"packaged-native/crates/{mod_id}"
        crate = bridge / member
        entries = (SCRIPTS / "packaged_entry.rs").read_text(encoding="utf-8")
        entries = entries.replace('"__NATIVE_BUILD_IDENTITY__"', STAGING["rust_string"](identity))
        entries = entries.replace(
            "/* BUILT_MOD */",
            f'BuiltMod {{ id: "{mod_id}", version: {STAGING["rust_string"](version)}, '
            f'side: mod_api::Side::{manifest["side"].capitalize()} }}',
        )
        (crate / "src/horizon_uwp_packaged_entry").mkdir(parents=True)
        (crate / "src/lib.rs").write_bytes(
            files["src/lib.rs"] + b"\nmod horizon_uwp_packaged_entry;\n"
        )
        (crate / "src/horizon_uwp_packaged_entry/mod.rs").write_text(entries, encoding="utf-8")
        (crate / "src/horizon_uwp_packaged_entry/packaged_abi.rs").write_bytes(
            (SCRIPTS / "packaged_abi.rs").read_bytes()
        )
        (crate / "Cargo.toml").write_text(
            f'[package]\nname = "{name}"\nversion = "{package["version"]}"\n'
            f'edition = "{package["edition"]}"\npublish = false\n\n'
            f'[lib]\nname = "{library}"\ncrate-type = ["staticlib"]\n\n'
            '[dependencies]\nhorizon-mod-api = { path = "../../../../../crates/horizon-mod-api" }\n',
            encoding="utf-8",
        )
        # This build script is supplied by the Builder, never copied from a mod project.
        (crate / "build.rs").write_text(
            'fn main() {\n'
            '    let rustc = std::process::Command::new(std::env::var_os("RUSTC").unwrap())\n'
            '        .arg("-vV").output().expect("run rustc");\n'
            '    let version = String::from_utf8(rustc.stdout).expect("rustc output");\n'
            f'    assert!(rustc.status.success() && version.lines().any(|line| line == "release: {TOOLCHAIN}"),\n'
            '        "native mod requires the pinned UWP Rust compiler");\n'
            f'    assert_eq!(std::env::var("TARGET").unwrap(), "{TARGETS[architecture]}",\n'
            '        "native mod requires the selected UWP architecture");\n'
            '}\n',
            encoding="utf-8",
        )
        snapshot = output / "horizon-native-mods" / mod_id
        (snapshot / "src").mkdir(parents=True)
        for relative, data in files.items():
            (snapshot / relative).write_bytes(data)
        added.append(
            f'[[package]]\nname = "{name}"\nversion = "{package["version"]}"\n'
            'dependencies = [\n "horizon-mod-api",\n]\n\n'
        )
        members.append(json.dumps(member))
        mods.append({
            "id": mod_id, "version": version, "identity": identity,
            "cargo_package": name, "static_library": library + ".lib", "dll": library + ".dll",
            "source_sha256": {relative: hashlib.sha256(data).hexdigest()
                              for relative, data in files.items()},
        })
    cargo_path.write_text(STAGING["replace_once"](
        cargo, "[workspace]\n", "[workspace]\nmembers = [" + ", ".join(members) + "]\n"
    ), encoding="utf-8")
    prefix, *blocks = re.split(r"(?=^\[\[package\]\]$)", lock, flags=re.MULTILINE)
    lock_path.write_text(prefix + "".join(sorted(
        (block.rstrip() + "\n\n" for block in blocks + added),
        key=lambda block: block.splitlines()[1],
    )).rstrip() + "\n", encoding="utf-8")
    record = {
        "architecture": architecture, "rust_target": TARGETS[architecture], "rust_toolchain": TOOLCHAIN,
        "game_build_id": game_build_id, "cargo_lock_sha256": hashlib.sha256(lock_path.read_bytes()).hexdigest(),
        "mods": mods,
    }
    (output / "build-record.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(f"Staged {len(mods)} separate native mod library project(s).")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--architecture", choices=TARGETS, required=True)
    parser.add_argument("--game-build-id", required=True)
    parser.add_argument("--trust-native-source", action="store_true", required=True)
    parser.add_argument("--source", type=Path, action="append", required=True)
    args = parser.parse_args()
    try:
        stage(args.workspace, args.architecture, args.source, args.game_build_id)
    except (OSError, ValueError, KeyError) as error:
        parser.exit(1, f"Packaged native source staging failed: {error}\n")


if __name__ == "__main__":
    main()
