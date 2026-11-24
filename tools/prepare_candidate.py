#!/usr/bin/env python3
"""Prepare unsigned development artifacts from a committed UWP repository tree."""

import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import tarfile
import zipfile


UPSTREAM = "f42ef42ef6ceccb3c6a20bea2481d87949a51c81"
NIGHTLY_UPSTREAM = "0b14f8acfd97fa2c750e42795249dcc981178ca4"
NIGHTLY_UPSTREAM_ARCHIVE_SHA256 = "1a73fb817549e9b0ec8870b8aec7efd1ea4e274e8be7563411c053aee6ad48ef"
NOV20_UPSTREAM = "0a875da91dbb0fdcb7ba8c42c11341aa229af51a"
NOV20_UPSTREAM_ARCHIVE_SHA256 = "abd6a91c41407522f9b0ec43d16555f46cd76832625645658bee57e2ab49da3d"
LEGACY_NARRATOR_UPSTREAM = "e797c725ad99ef311db216de7f41a2e6593353db"
LEGACY_NARRATOR_PATCH_PATH = "patches/main-2026-11-09/horizon-uwp-0.1.5.patch"
NOV20_INITIAL_PATCH = "0.1.13"
NOV20_DRS_PATCH = "0.1.14"
NOV20_PATCH = "0.1.15"
HDR_PATCH = "0.1.16"
CLASSIC_PACK_GUARD_PATCH = "0.1.17"
GRAPHICS_COMPATIBILITY_PATCH = "0.1.18"
NARRATOR_PATCH = "0.1.19"
HDR_PATCHES = (HDR_PATCH, CLASSIC_PACK_GUARD_PATCH, GRAPHICS_COMPATIBILITY_PATCH, NARRATOR_PATCH)
NOV20_PATCHES = (NOV20_INITIAL_PATCH, NOV20_DRS_PATCH, NOV20_PATCH, *HDR_PATCHES)
NOV20_INITIAL_RECIPE = ("0.1.14", "1.0.7")
NOV20_WASM_STANDARD_LIBRARY_SHA256 = "c1d04e08fb402134fb849ae6cb5d5af518c1e5612d715a75b3757c2a5bd3ec5f"
NIGHTLY_RECIPES = {
    "0.1.5": ("0.1.6", "1.0.4"),
    "0.1.6": ("0.1.7", "1.0.5"),
    "0.1.7": ("0.1.8", "1.0.6"),
    "0.1.8": ("0.1.9", "1.0.6"),
    "0.1.9": ("0.1.10", "1.0.6"),
    "0.1.10": ("0.1.11", "1.0.6"),
    "0.1.11": ("0.1.12", "1.0.6"),
    "0.1.12": ("0.1.13", "1.0.6"),
    NOV20_INITIAL_PATCH: ("0.1.15", "1.0.8"),
    NOV20_DRS_PATCH: ("0.1.16", "1.0.8"),
    NOV20_PATCH: ("0.1.17", "1.0.9"),
    HDR_PATCH: ("0.1.18", "1.0.10"),
    CLASSIC_PACK_GUARD_PATCH: ("0.1.18", "1.0.10"),
    GRAPHICS_COMPATIBILITY_PATCH: ("0.1.18", "1.0.10"),
    NARRATOR_PATCH: ("0.1.19", "1.0.10"),
}
WASMTIME_ARCHIVE_SHA256 = "9e32e3ce9a15d2ddedfeae38437a4fe52ed9c59ac878e3131e5a840068faf44e"
WASM_STANDARD_LIBRARY_SHA256 = "123d39c44516ea2fed47f9a8099b5a96e35a675cf802a7e9aa4d149cb781296b"
WGPU_ARCHIVE_SHA256 = "4c8769b47eea565ca994d4699ff72e70f6647613b6205c8daa52f2b04a743eaa"
FIXED_WGPU_PATCH_VERSIONS = ("0.1.11", "0.1.12", *NOV20_PATCHES)
FIXED_WGPU_ARCHIVE_SHA256 = "8de8795116083533812728a855144a5f5e229d0562ef07990393bc5124c48c2e"
HDR_WGPU_PATCH_PATH = "patches/wgpu-dx11/dx12-uwp-hdr-surface.patch"
HDR_WGPU_PATCH_SHA256 = "4c31f9349e28d294fa6d1663a6c4d3cacc134a9a4c8b1e2aea63688230e0c934"
WGPU_PATCH_PATH = "patches/wgpu-dx11/reuse-upload-buffer-shadow.patch"
WGPU_PATCH_SHA256 = "ee0a57e971221a3b705ca8434b67ea2f2909fb8b3b21444163e0bf5be44046e4"
PATCH_PATHS = {
    "0.1.0": "patches/nightly-2026-11-03/horizon-uwp.patch",
    "0.1.1": "patches/nightly-2026-11-03/horizon-uwp-0.1.1.patch",
    "0.1.2": "patches/main-2026-11-09/horizon-uwp-0.1.2.patch",
    "0.1.3": "patches/main-2026-11-09/horizon-uwp-0.1.3.patch",
    "0.1.4": "patches/main-2026-11-09/horizon-uwp-0.1.4.patch",
    "0.1.5": "patches/nightly-2026-11-18/horizon-uwp-0.1.5.patch",
    "0.1.6": "patches/nightly-2026-11-18/horizon-uwp-0.1.6.patch",
    "0.1.7": "patches/nightly-2026-11-18/horizon-uwp-0.1.7.patch",
    "0.1.8": "patches/nightly-2026-11-18/horizon-uwp-0.1.8.patch",
    "0.1.9": "patches/nightly-2026-11-18/horizon-uwp-0.1.9.patch",
    "0.1.10": "patches/nightly-2026-11-18/horizon-uwp-0.1.10.patch",
    "0.1.11": "patches/nightly-2026-11-18/horizon-uwp-0.1.11.patch",
    "0.1.12": "patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch",
    NOV20_INITIAL_PATCH: "patches/nightly-2026-11-20/horizon-uwp-0.1.13.patch",
    NOV20_DRS_PATCH: "patches/nightly-2026-11-20/horizon-uwp-0.1.14.patch",
    NOV20_PATCH: "patches/nightly-2026-11-20/horizon-uwp-0.1.15.patch",
    HDR_PATCH: "patches/nightly-2026-11-20/horizon-uwp-0.1.16.patch",
    CLASSIC_PACK_GUARD_PATCH: "patches/nightly-2026-11-20/horizon-uwp-0.1.17.patch",
    GRAPHICS_COMPATIBILITY_PATCH: "patches/nightly-2026-11-20/horizon-uwp-0.1.18.patch",
    NARRATOR_PATCH: "patches/nightly-2026-11-20/horizon-uwp-0.1.19.patch",
}
REPOSITORY = "https://github.com/megabytesme/MCHorizonUWP"


def git(*args):
    return subprocess.check_output(["git", *args])


def digest(data):
    return hashlib.sha256(data).hexdigest()


def same_text_content(left, right):
    if left == right:
        return True
    if b"\0" in left or b"\0" in right:
        return False
    return left.replace(b"\r\n", b"\n") == right.replace(b"\r\n", b"\n")


def verify_upstream(archive_path, repository, commit):
    tar_bytes = subprocess.check_output(["git", "-C", str(repository), "-c", "core.autocrlf=false", "archive", "--format=tar", commit])
    expected = {}
    with tarfile.open(fileobj=io.BytesIO(tar_bytes)) as source:
        for item in source:
            if item.isdir():
                continue
            if item.isfile():
                expected[item.name] = source.extractfile(item).read()
            elif item.issym():
                expected[item.name] = item.linkname.encode("utf-8")
            else:
                raise ValueError(f"Unsupported upstream entry: {item.name}")
    found = {}
    prefix = None
    with zipfile.ZipFile(archive_path) as source:
        for item in source.infolist():
            if item.is_dir():
                continue
            parts = item.filename.split("/")
            if len(parts) < 2 or any(part in ("", ".", "..") for part in parts):
                raise ValueError("Invalid upstream ZIP path")
            prefix = prefix or parts[0]
            if prefix != parts[0]:
                raise ValueError("Upstream ZIP has multiple roots")
            name = "/".join(parts[1:])
            if name in found:
                raise ValueError("Duplicate upstream ZIP entry")
            found[name] = source.read(item)
    missing = expected.keys() - found.keys()
    extra = found.keys() - expected.keys()
    different = [name for name, data in expected.items()
                 if name in found and not same_text_content(found[name], data)]
    if missing or extra or different:
        samples = sorted(missing)[:2] + sorted(extra)[:2] + sorted(different)[:2]
        raise ValueError("Upstream ZIP contents do not match the pinned Git commit: " + ", ".join(samples))


NARRATOR_EXPORTS = tuple(f"horizon_uwp_narrator_{name}".encode("ascii") for name in
                         ("open", "close", "say", "poll", "clear", "voices", "set_voice"))
SCREENSHOT_EXPORT = b"horizon_uwp_publish_screenshot"


def verify_narrator_host_kit(root):
    """Reject older host kits before pairing them with the 0.1.19 bridge."""
    for architecture in ("arm", "arm64", "x86", "x64"):
        for filename in ("HorizonUWP.exe", "HorizonUWPHost.lib"):
            path = root / architecture / filename
            if not path.is_file():
                raise ValueError(f"Prebuilt host kit is incomplete: {architecture}/{filename}")
            data = path.read_bytes()
            if any(name not in data for name in NARRATOR_EXPORTS):
                raise ValueError(f"Prebuilt host is missing narrator exports: {architecture}/{filename}")
            if SCREENSHOT_EXPORT not in data:
                raise ValueError(f"Prebuilt host is missing screenshot publication: {architecture}/{filename}")


def client_archive(revision, host_artifacts=None, wgpu_archive=None, wasm_inputs=(), fixed_wgpu=False, hdr_wgpu=False, narrator=False):
    archive = git("archive", "--format=tar", revision, "uwp", "LICENSE", "THIRD_PARTY_NOTICES.md")
    output = io.BytesIO()
    with tarfile.open(fileobj=io.BytesIO(archive)) as source, zipfile.ZipFile(
        output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9
    ) as result:
        for entry in source:
            if entry.isdir() or entry.name.startswith("uwp/builder/"):
                continue
            if not entry.isfile():
                raise ValueError(f"Unsupported archive entry: {entry.name}")
            name = "notices/" + entry.name if entry.name in ("LICENSE", "THIRD_PARTY_NOTICES.md") else entry.name
            if not name.startswith(("uwp/", "notices/")) or ".." in Path(name).parts:
                raise ValueError(f"Invalid archive path: {name}")
            item = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            item.compress_type = zipfile.ZIP_DEFLATED
            item.external_attr = 0o100644 << 16
            result.writestr(item, source.extractfile(entry).read())
        if wgpu_archive is not None:
            wgpu_zip = wgpu_archive.read_bytes()
            expected = FIXED_WGPU_ARCHIVE_SHA256 if fixed_wgpu else WGPU_ARCHIVE_SHA256
            if digest(wgpu_zip) != expected:
                raise ValueError("The Nightly wgpu archive does not match its pinned SHA-256")
            wgpu_inputs = [("wgpu.zip", wgpu_zip)]
            if not fixed_wgpu:
                wgpu_patch = git("show", f"{revision}:{WGPU_PATCH_PATH}")
                if digest(wgpu_patch) != WGPU_PATCH_SHA256:
                    raise ValueError("The Nightly wgpu patch does not match its pinned SHA-256")
                wgpu_inputs.append(("reuse-upload-buffer-shadow.patch", wgpu_patch))
            if hdr_wgpu:
                hdr_patch = git("show", f"{revision}:{HDR_WGPU_PATCH_PATH}")
                if digest(hdr_patch) != HDR_WGPU_PATCH_SHA256:
                    raise ValueError("The HDR wgpu patch does not match its pinned SHA-256")
                wgpu_inputs.append(("dx12-uwp-hdr-surface.patch", hdr_patch))
            for name, data in wgpu_inputs:
                item = zipfile.ZipInfo(f"uwp/wgpu-dx11/{name}", (1980, 1, 1, 0, 0, 0))
                item.external_attr = 0o100644 << 16
                result.writestr(item, data)
        for name, path, checksum in wasm_inputs:
            data = path.read_bytes()
            if digest(data) != checksum:
                raise ValueError(f"{name} does not match the pinned Nightly SHA-256")
            item = zipfile.ZipInfo(f"uwp/wasm-abi-probe/{name}", (1980, 1, 1, 0, 0, 0))
            item.compress_type = zipfile.ZIP_DEFLATED
            item.external_attr = 0o100644 << 16
            result.writestr(item, data)
        if host_artifacts is not None:
            root = Path(host_artifacts).resolve(strict=True)
            expected = ["README.txt"]
            for architecture in ("arm", "arm64", "x86", "x64"):
                expected.extend([
                    f"{architecture}/HorizonUWP.exe",
                    f"{architecture}/HorizonUWP.winmd",
                    f"{architecture}/HorizonUWPHost.lib",
                    f"{architecture}/dead-import-sentinels.obj",
                    *(f"{architecture}/link/{name}" for name in ("libcmt.lib", "tls_index.obj")),
                ])
            for relative in expected:
                if not (root / relative).is_file():
                    raise ValueError(f"Prebuilt host kit is incomplete: {relative}")
            if narrator:
                verify_narrator_host_kit(root)
            for path in sorted(root.rglob("*")):
                if path.is_symlink():
                    raise ValueError("Prebuilt host kit cannot contain symbolic links")
                if not path.is_file():
                    continue
                relative = path.relative_to(root).as_posix()
                if relative.startswith("/") or ".." in Path(relative).parts or "\\" in relative:
                    raise ValueError(f"Invalid prebuilt host kit path: {relative}")
                name = f"uwp/host/prebuilt/{relative}"
                item = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
                item.compress_type = zipfile.ZIP_DEFLATED
                item.external_attr = 0o100644 << 16
                result.writestr(item, path.read_bytes())
    return output.getvalue()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--revision", default="HEAD", help="Committed repository revision")
    parser.add_argument("--upstream-commit", help="Upstream Horizon commit pinned by the patch")
    parser.add_argument("--patch-version", default="0.1.0", help="Patch release version (three numeric components)")
    parser.add_argument("--patch-path", help="Committed patch path under patches/ (defaults to the known path for this version)")
    parser.add_argument("--client-version", help="Client release version (three numeric components)")
    parser.add_argument("--builder-version", help="Builder version for the candidate compatibility rule")
    parser.add_argument("--patch-only", action="store_true", help="Prepare a patch and compatibility rule for an already released client")
    parser.add_argument("--client-asset-id", help="GitHub release asset ID; uses the binary API endpoint instead of the release-download redirect")
    parser.add_argument("--host-artifacts", type=Path, help="Prepared host kit to include in the client archive")
    parser.add_argument("--wgpu-archive", type=Path, help="Pinned wgpu source ZIP required by Nightly clients")
    parser.add_argument("--wasmtime-archive", type=Path, help="Pinned crates.io Wasmtime 50.0.2 archive")
    parser.add_argument("--wasm-standard-library", type=Path, help="Pinned Nightly Horizon Wasm standard library")
    parser.add_argument("--catalog-input", type=Path, default=Path("catalog/catalog-v1.json"),
                        help="Existing signed catalog whose older entries and rules must be retained")
    parser.add_argument("--output", required=True, type=Path, help="New output directory")
    parser.add_argument("--upstream-archive", required=True, type=Path, help="Verified source ZIP for the pinned upstream commit")
    parser.add_argument("--upstream-repo", required=True, type=Path, help="Git repository containing the pinned upstream commit")
    args = parser.parse_args()
    legacy_narrator = args.patch_path == LEGACY_NARRATOR_PATCH_PATH
    if legacy_narrator and args.patch_version != "0.1.5":
        parser.error("The November 9 narrator source requires patch version 0.1.5")
    nightly = None if legacy_narrator else NIGHTLY_RECIPES.get(args.patch_version)
    nov20 = args.patch_version in NOV20_PATCHES
    nightly_source = NOV20_UPSTREAM if nov20 else NIGHTLY_UPSTREAM
    nightly_archive_hash = NOV20_UPSTREAM_ARCHIVE_SHA256 if nov20 else NIGHTLY_UPSTREAM_ARCHIVE_SHA256
    default_upstream = nightly_source if nightly else UPSTREAM
    if legacy_narrator:
        default_upstream = LEGACY_NARRATOR_UPSTREAM
    upstream_commit = (args.upstream_commit or default_upstream).lower()
    if legacy_narrator and upstream_commit != LEGACY_NARRATOR_UPSTREAM:
        parser.error("The November 9 narrator patch requires its pinned Horizon source")
    default_client, default_builder = nightly or ("0.1.5" if legacy_narrator else "0.1.0", "1.0.3")
    client_version = args.client_version or default_client
    builder_version = args.builder_version or default_builder
    wasm_inputs = ()
    if args.patch_version in ("0.1.7", "0.1.8", "0.1.9", "0.1.10", *FIXED_WGPU_PATCH_VERSIONS) and not args.patch_only:
        if args.wasmtime_archive is None or args.wasm_standard_library is None:
            parser.error(f"Patch {args.patch_version} requires both pinned Wasm inputs")
        wasm_inputs = (
            ("wasmtime-50.0.2.crate", args.wasmtime_archive, WASMTIME_ARCHIVE_SHA256),
            ("horizon-wasm-std.wasm", args.wasm_standard_library,
             NOV20_WASM_STANDARD_LIBRARY_SHA256 if nov20 else WASM_STANDARD_LIBRARY_SHA256),
        )
    elif args.wasmtime_archive is not None or args.wasm_standard_library is not None:
        parser.error("Wasm inputs are only accepted when building a client archive with the Wasm runtime")
    if nightly:
        recipes = (nightly, NOV20_INITIAL_RECIPE) if args.patch_version == NOV20_INITIAL_PATCH else (nightly,)
        if upstream_commit != nightly_source or (client_version, builder_version) not in recipes:
            raise ValueError(f"Nightly patch {args.patch_version} requires its pinned source, "
                             f"client {default_client} and Builder {default_builder}")
        if not args.patch_only and args.wgpu_archive is None:
            raise ValueError("Preparing a Nightly client requires --wgpu-archive")
    elif (args.wgpu_archive is not None or upstream_commit in (NIGHTLY_UPSTREAM, NOV20_UPSTREAM)
          or client_version == NOV20_INITIAL_RECIPE[0]
          or any(client_version == client for client, _ in NIGHTLY_RECIPES.values())):
        raise ValueError("The Nightly source, client and wgpu inputs require a Nightly patch")

    def upper_version(value):
        if not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", value):
            raise ValueError("Versions must have three numeric components without leading zeroes")
        major, minor, patch_number = map(int, value.split("."))
        if max(major, minor) > 2_147_483_647 or patch_number >= 2_147_483_647:
            raise ValueError("Version exceeds catalog numeric limits")
        return f"{major}.{minor}.{patch_number + 1}"
    patch_upper = upper_version(args.patch_version)
    client_upper = upper_version(client_version)
    builder_upper = upper_version(builder_version)
    patch_path = args.patch_path or PATCH_PATHS.get(args.patch_version)
    if (not patch_path or not patch_path.startswith("patches/") or "\\" in patch_path
            or ".." in Path(patch_path).parts or "." in Path(patch_path).parts):
        raise ValueError("Provide a supported patch version or a safe committed patch path under patches/.")
    revision = git("rev-parse", "--verify", args.revision + "^{commit}").decode().strip()
    if not re.fullmatch(r"[0-9a-f]{40}", upstream_commit):
        raise ValueError("Invalid upstream Horizon commit")
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("Invalid Git commit")
    if args.client_asset_id is not None and not re.fullmatch(r"[1-9][0-9]*", args.client_asset_id):
        raise ValueError("The GitHub client asset ID must be a positive decimal number")
    patch = git("show", f"{revision}:{patch_path}")
    if not args.patch_only and args.host_artifacts is None:
        raise ValueError("Every client release requires --host-artifacts for its prebuilt hosts")
    client = None if args.patch_only else client_archive(
        revision, args.host_artifacts, args.wgpu_archive, wasm_inputs,
        fixed_wgpu=args.patch_version in FIXED_WGPU_PATCH_VERSIONS,
        hdr_wgpu=args.patch_version in HDR_PATCHES, narrator=args.patch_version == NARRATOR_PATCH
    )
    verify_upstream(args.upstream_archive, args.upstream_repo, upstream_commit)
    upstream_hash = digest(args.upstream_archive.read_bytes())
    if nightly and upstream_hash != nightly_archive_hash:
        raise ValueError("Nightly release inputs require the verified GitHub codeload ZIP")
    catalog = json.loads(args.catalog_input.read_text(encoding="utf-8"))
    if catalog.get("schema_version") != 1:
        raise ValueError("The existing compatibility catalog has an unsupported schema")
    base = next((item for item in catalog["patches"] if item["upstream_commit"] == upstream_commit), None)
    upstream = base["upstream"] if base else {
        "url": f"https://github.com/horizon-rust/horizon/archive/{upstream_commit}.zip", "sha256": upstream_hash
    }
    patch_entry = {"version": args.patch_version, "upstream_commit": upstream_commit, "bridge": 1,
                   "upstream": upstream,
                   "artifact": {"url": f"https://raw.githubusercontent.com/megabytesme/MCHorizonUWP/{revision}/{patch_path}",
                                "sha256": digest(patch)}}
    matching_patch = [item for item in catalog["patches"] if item["version"] == args.patch_version]
    if matching_patch:
        retained = matching_patch[0]
        same_identity = (retained.get("upstream_commit") == patch_entry["upstream_commit"]
                         and retained.get("bridge") == patch_entry["bridge"]
                         and retained.get("upstream") == patch_entry["upstream"]
                         and retained.get("artifact", {}).get("sha256") == patch_entry["artifact"]["sha256"])
        if not same_identity:
            raise ValueError("The candidate patch or upstream archive differs from the retained catalog entry")
        patch_entry = retained
    else:
        catalog["patches"].append(patch_entry)
    if args.patch_only:
        if not any(item["version"] == client_version and item["bridge"] == patch_entry["bridge"]
                   for item in catalog["clients"]):
            raise ValueError(f"Client {client_version} is not present in the input catalog")
    else:
        if any(item["version"] == client_version for item in catalog["clients"]):
            raise ValueError(f"Client {client_version} already exists in the input catalog")
        client_url = (f"https://api.github.com/repos/megabytesme/MCHorizonUWP/releases/assets/{args.client_asset_id}"
                      if args.client_asset_id else
                      f"{REPOSITORY}/releases/download/client-v{client_version}/uwp-client-{client_version}.zip")
        catalog["clients"].append({"version": client_version, "bridge": 1,
                                   "artifact": {"url": client_url, "sha256": digest(client)}})
    catalog["rules"].append({"patch": {"min_inclusive": args.patch_version, "max_exclusive": patch_upper},
                             "client": {"min_inclusive": client_version, "max_exclusive": client_upper},
                             "builder": {"min_inclusive": builder_version, "max_exclusive": builder_upper},
                             "architectures": ["x86", "x64", "arm", "arm64"]})
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / f"horizon-uwp-{args.patch_version}.patch").write_bytes(patch)
    if client is not None:
        (args.output / f"uwp-client-{client_version}.zip").write_bytes(client)
    (args.output / "catalog.UNSIGNED.json").write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Prepared unsigned development artifacts from {revision} in {args.output}")
    print("No release or test evidence was published; authenticate the catalog before use.")


if __name__ == "__main__":
    main()
