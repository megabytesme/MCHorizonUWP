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
    tar_bytes = subprocess.check_output(["git", "-C", str(repository), "archive", "--format=tar", UPSTREAM])
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


def client_archive(revision):
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
    return output.getvalue()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--revision", default="HEAD", help="Committed repository revision")
    parser.add_argument("--output", required=True, type=Path, help="New output directory")
    parser.add_argument("--upstream-archive", required=True, type=Path, help="Verified source ZIP for the pinned upstream commit")
    parser.add_argument("--upstream-repo", required=True, type=Path, help="Git repository containing the pinned upstream commit")
    args = parser.parse_args()
    revision = git("rev-parse", "--verify", args.revision + "^{commit}").decode().strip()
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("Invalid Git commit")
    patch = git("show", f"{revision}:{PATCH_PATH}")
    client = client_archive(revision)
    verify_upstream(args.upstream_archive, args.upstream_repo)
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "horizon-uwp-0.1.0.patch").write_bytes(patch)
    (args.output / "uwp-client-0.1.0.zip").write_bytes(client)
    upstream_hash = digest(args.upstream_archive.read_bytes())
    catalog = {
        "schema_version": 1,
        "patches": [{"version": "0.1.0", "upstream_commit": UPSTREAM, "bridge": 1,
                     "upstream": {"url": f"https://github.com/horizon-rust/horizon/archive/{UPSTREAM}.zip", "sha256": upstream_hash},
                     "artifact": {"url": f"{REPOSITORY}/releases/download/patch-v0.1.0/horizon-uwp-0.1.0.patch", "sha256": digest(patch)}}],
        "clients": [{"version": "0.1.0", "bridge": 1,
                     "artifact": {"url": f"{REPOSITORY}/releases/download/client-v0.1.0/uwp-client-0.1.0.zip", "sha256": digest(client)}}],
        "rules": [{"patch": {"min_inclusive": "0.1.0", "max_exclusive": "0.1.1"},
                   "client": {"min_inclusive": "0.1.0", "max_exclusive": "0.1.1"},
                   "builder": {"min_inclusive": "0.1.0", "max_exclusive": "0.1.1"},
                   "architectures": ["x86", "x64", "arm", "arm64"]}],
        "evidence": [],
    }
    (args.output / "catalog.UNSIGNED.json").write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared unsigned development artifacts from {revision} in {args.output}")
    print("No release or test evidence was published; authenticate the catalog before use.")


if __name__ == "__main__":
    main()
