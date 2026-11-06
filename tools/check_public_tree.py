#!/usr/bin/env python3
"""Reject common private game files and signing keys from the Git tree."""

import argparse
from pathlib import PurePosixPath
import subprocess
import sys


PRIVATE_DIRECTORIES = {".local", "decompiled", "minecraft", "run", "logs"}
PRIVATE_EXTENSIONS = {".jar", ".java", ".class", ".jfr", ".hprof", ".pfx", ".p12", ".jks", ".keystore", ".key"}
PRIVATE_NAMES = {"horizon-data.bin"}


def prohibited(path: str) -> bool:
    item = PurePosixPath(path)
    return (
        bool(item.parts and item.parts[0].lower() == "assets")
        or bool(PRIVATE_DIRECTORIES.intersection(part.lower() for part in item.parts[:-1]))
        or item.suffix.lower() in PRIVATE_EXTENSIONS
        or item.name.lower() in PRIVATE_NAMES
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--history", action="store_true", help="check paths in all fetched Git history")
    args = parser.parse_args()
    command = ["git", "--no-replace-objects"]
    command += (["log", "--all", "--format=", "--name-only", "-z", "-m"] if args.history else ["ls-files", "-z"])
    output = subprocess.check_output(command)
    paths = {path.lstrip("\n") for path in output.decode("utf-8").split("\0") if path}
    invalid = sorted(path for path in paths if prohibited(path))
    if invalid:
        print("Private game files or signing keys must not be committed:", file=sys.stderr)
        for path in invalid:
            print(f"  {path}", file=sys.stderr)
        return 1
    print(f"Public tree check passed ({len(paths)} paths checked).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
