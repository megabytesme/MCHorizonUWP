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
PATCH_PATH = "patches/nightly-2026-11-03/horizon-uwp.patch"
REPOSITORY = "https://github.com/megabytesme/MCHorizonUWP"


def git(*args):
    return subprocess.check_output(["git", *args])


def digest(data):
    return hashlib.sha256(data).hexdigest()


def verify_upstream(archive_path, repository):
    tar_bytes = subprocess.check_output(["git", "-C", str(repository), "-c", "core.autocrlf=false", "archive", "--format=tar", UPSTREAM])
    expected = {}
    with tarfile.open(fileobj=io.BytesIO(tar_bytes)) as source:
        for item in source:
            if item.isdir():
                continue
            if item.isfile():
                expected[item.name] = digest(source.extractfile(item).read())
            elif item.issym():
                expected[item.name] = digest(item.linkname.encode("utf-8"))
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
            found[name] = digest(source.read(item))
    if found != expected:
        raise ValueError("Upstream ZIP contents do not match the pinned Git commit")


def client_archive(revision, host_artifacts=None):
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
        if host_artifacts is not None:
            root = Path(host_artifacts).resolve(strict=True)
            expected = ["README.txt"]
            for architecture in ("arm", "arm64", "x86", "x64"):
                expected.extend([
                    f"{architecture}/HorizonUWP.exe",
                    f"{architecture}/HorizonUWPHost.lib",
                    f"{architecture}/dead-import-sentinels.obj",
                    *(f"{architecture}/link/{name}" for name in ("libcmt.lib", "atexit.lib", "tls_index.obj")),
                ])
            for relative in expected:
                if not (root / relative).is_file():
                    raise ValueError(f"Prebuilt host kit is incomplete: {relative}")
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
    parser.add_argument("--client-version", default="0.1.0", help="Client release version (three numeric components)")
    parser.add_argument("--builder-version", default="0.1.0", help="Builder version for the candidate compatibility rule")
    parser.add_argument("--host-artifacts", type=Path, help="Prepared four-architecture host kit to include in the client archive")
    parser.add_argument("--catalog-input", type=Path, default=Path("catalog/catalog-v1.json"),
                        help="Existing signed catalog whose older entries and rules must be retained")
    parser.add_argument("--output", required=True, type=Path, help="New output directory")
    parser.add_argument("--upstream-archive", required=True, type=Path, help="Verified source ZIP for the pinned upstream commit")
    parser.add_argument("--upstream-repo", required=True, type=Path, help="Git repository containing the pinned upstream commit")
    args = parser.parse_args()
    def upper_version(value):
        if not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", value):
            raise ValueError("Versions must have three numeric components without leading zeroes")
        major, minor, patch_number = map(int, value.split("."))
        if max(major, minor) > 2_147_483_647 or patch_number >= 2_147_483_647:
            raise ValueError("Version exceeds catalog numeric limits")
        return f"{major}.{minor}.{patch_number + 1}"
    client_upper = upper_version(args.client_version)
    builder_upper = upper_version(args.builder_version)
    revision = git("rev-parse", "--verify", args.revision + "^{commit}").decode().strip()
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("Invalid Git commit")
    patch = git("show", f"{revision}:{PATCH_PATH}")
    if args.client_version == "0.1.2" and args.host_artifacts is None:
        raise ValueError("Client 0.1.2 requires --host-artifacts for the four prebuilt host kits")
    client = client_archive(revision, args.host_artifacts)
    verify_upstream(args.upstream_archive, args.upstream_repo)
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "horizon-uwp-0.1.0.patch").write_bytes(patch)
    (args.output / f"uwp-client-{args.client_version}.zip").write_bytes(client)
    upstream_hash = digest(args.upstream_archive.read_bytes())
    catalog = json.loads(args.catalog_input.read_text(encoding="utf-8"))
    if catalog.get("schema_version") != 1:
        raise ValueError("The existing compatibility catalog has an unsupported schema")
    patch_entry = {"version": "0.1.0", "upstream_commit": UPSTREAM, "bridge": 1,
                   "upstream": {"url": f"https://github.com/horizon-rust/horizon/archive/{UPSTREAM}.zip", "sha256": upstream_hash},
                   "artifact": {"url": f"{REPOSITORY}/releases/download/patch-v0.1.0/horizon-uwp-0.1.0.patch", "sha256": digest(patch)}}
    matching_patch = [item for item in catalog["patches"] if item["version"] == "0.1.0"]
    if matching_patch:
        if matching_patch[0] != patch_entry:
            raise ValueError("The candidate patch or upstream archive differs from the retained catalog entry")
    else:
        catalog["patches"].append(patch_entry)
    if any(item["version"] == args.client_version for item in catalog["clients"]):
        raise ValueError(f"Client {args.client_version} already exists in the input catalog")
    catalog["clients"].append({"version": args.client_version, "bridge": 1,
                               "artifact": {"url": f"{REPOSITORY}/releases/download/client-v{args.client_version}/uwp-client-{args.client_version}.zip",
                                            "sha256": digest(client)}})
    catalog["rules"].append({"patch": {"min_inclusive": "0.1.0", "max_exclusive": "0.1.1"},
                             "client": {"min_inclusive": args.client_version, "max_exclusive": client_upper},
                             "builder": {"min_inclusive": args.builder_version, "max_exclusive": builder_upper},
                             "architectures": ["x86", "x64", "arm", "arm64"]})
    (args.output / "catalog.UNSIGNED.json").write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared unsigned development artifacts from {revision} in {args.output}")
    print("No release or test evidence was published; authenticate the catalog before use.")


if __name__ == "__main__":
    main()
