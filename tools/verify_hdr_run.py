"""Check an archived D3D12 HDR run against its APPX receipt, logs and PNGs."""

import argparse
from datetime import datetime
import hashlib
import json
from pathlib import Path
import re
import struct
from xml.etree import ElementTree
import zipfile
import zlib

from hdr_check_png import inspect, require


def sha256(stream):
    return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_package(appx, receipt, run):
    if isinstance(receipt.get("package"), dict):
        receipt = receipt["package"]
    expected = receipt.get("package_sha256", receipt.get("sha256"))
    with appx.open("rb") as stream:
        require(sha256(stream) == expected.lower(), "APPX SHA-256 differs from receipt")
        stream.seek(0)
        with zipfile.ZipFile(stream) as package:
            identity = ElementTree.fromstring(package.read("AppxManifest.xml")).find("{*}Identity").attrib
            publisher = hashlib.sha256(identity["Publisher"].encode("utf-16-le")).digest()[:8]
            bits = f"{int.from_bytes(publisher, 'big') << 1:065b}"
            alphabet = "0123456789abcdefghjkmnpqrstvwxyz"
            publisher_id = "".join(alphabet[int(bits[i:i + 5], 2)] for i in range(0, 65, 5))
            full_name = "_".join((identity["Name"], identity["Version"], identity["ProcessorArchitecture"],
                                  identity.get("ResourceId", ""), publisher_id))
            require(run["package"] == full_name, "Run package full name differs from APPX manifest")
            for filename, field in (("HorizonUWP.exe", "host_sha256"),
                                    ("horizon_uwp_game_bridge.dll", "bridge_sha256")):
                with package.open(filename) as payload:
                    require(sha256(payload) == receipt[field].lower(), f"APPX {filename} differs from receipt")
    return expected


def utc(line):
    timestamp = datetime.fromisoformat(line.split()[0].replace("Z", "+00:00"))
    require(timestamp.tzinfo is not None, "Lifecycle timestamp has no timezone")
    return timestamp


def chain(key, hdr, checks, lifecycle, after, *, sdr_view=False):
    line = checks[key]
    require(line in lifecycle and utc(line) >= after, f"Missing or stale lifecycle entry for {key}")
    fields = dict(re.findall(r"(\w+)=([^ ]+)", line))
    expected = (10, 3, 1) if hdr else (87, 3, 0)
    require(" DXGI color configure: " in line and
            tuple(int(fields[k]) for k in ("format", "alpha", "space")) == expected,
            f"Unexpected panel format, alpha or color space for {key}")
    require(all(int(fields[k], 16) == 0 for k in ("desc_hr", "set_hr", "check_hr")) and
            int(fields["support"], 16) & 1, f"DXGI call or PRESENT support failed for {key}")
    view = int(fields["view_kind"])
    valid_views = (2,) if hdr else (0, 1) if sdr_view else (0, 1, 2)
    require(view in valid_views, f"Unexpected active-view kind for {key}")
    return utc(line)


def captures(directory, entries, hdr, game):
    expected = {"sdr", "hdr"} if hdr else {"sdr"}
    found = {}
    for entry in entries:
        name, original = entry["name"], entry["original"]
        require(re.fullmatch(r"[\w.-]+\.png", name) and re.fullmatch(r"[\w.-]+\.png", original),
                "Capture must name a local PNG")
        kind = "hdr" if original.endswith("-hdr.png") else "sdr"
        require(kind not in found, f"Duplicate {kind} capture")
        details = inspect(directory / name, kind)
        require(details["sha256"] == entry["sha256"].lower(), f"Capture hash differs for {name}")
        found[kind] = details | {"original": original}
    require(found.keys() == expected, "Incorrect SDR/HDR capture pair")
    primary = found["sdr"]["original"]
    require(any("Saved screenshot to " in line and line.endswith("\\" + primary) for line in game),
            f"Game log does not record capture {primary}")
    if hdr:
        companion = found["hdr"]["original"]
        require(companion == primary.removesuffix(".png") + "-hdr.png" and
                found["sdr"]["size"] == found["hdr"]["size"], "HDR companion does not match the SDR image")
        require(any("Saved HDR screenshot to " in line and line.endswith("\\" + companion) for line in game),
                f"Game log does not record capture {companion}")
    return found


def verify_run(appx, receipt, directory, case, expected_hdr, display):
    raw_result = (directory / "result.json").read_bytes()
    run = json.loads(raw_result)
    digest = verify_package(appx, receipt, run)
    result = {"appx_sha256": digest, "package": run["package"], "pid": run["pid"], "case": case,
              "display": run.get("actualDisplay"), "expected_hdr": expected_hdr, "pending": [],
              "evidence_sha256": {"result.json": hashlib.sha256(raw_result).hexdigest()}}
    if run.get("failure"):
        result["pending"].append(run["failure"])
        return result
    require(run.get("passed") is True and run.get("closed") is True and run["pid"] > 0,
            "Run did not record a successful, closed process")
    require(run["backend"] == "Dx12" and run["expectedHdr"] is expected_hdr and result["display"],
            "Unexpected backend, display or HDR mode")
    require(display is None or result["display"] == display, "Run reports a different display")
    checks = run["checks"]
    def read_log(name):
        raw = (directory / name).read_bytes()
        result["evidence_sha256"][name] = hashlib.sha256(raw).hexdigest()
        return raw.decode("utf-8-sig").splitlines()

    game = read_log("game.log")
    lifecycle = read_log("lifecycle.log")
    renderer = checks["renderer"]
    require(renderer in game and re.search(r"backend=Dx12,.*hardware=true.*software=false", renderer),
            "No hardware D3D12 renderer evidence")
    initial = chain("swapchain", expected_hdr, checks, lifecycle, utc(run["started"]))
    result["initial_chain"] = checks["swapchain"]
    if expected_hdr:
        first_hdr = checks["firstHdrFrame"]
        require(first_hdr in game and "HDR output presented its first frame" in first_hdr,
                "No first HDR frame evidence")
    result["initial_capture"] = captures(directory, checks["capture"], expected_hdr, game)

    if case == "device-recovery":
        removed = checks["removal"]
        require(removed in lifecycle and removed.endswith(" HDR QA removed reason=0x887A0005") and
                utc(removed) > initial,
                "No fresh DXGI_ERROR_DEVICE_REMOVED receipt")
        chain("recoveredSwapchain", expected_hdr, checks, lifecycle, utc(removed))
        result["removal"] = removed
        result["recovered_chain"] = checks["recoveredSwapchain"]
        recovered = checks["recovery"]
        require(recovered in game and "graphics device recovered; rebuilding world meshes" in recovered,
                "No renderer recovery evidence")
        recovery_log = game[game.index(recovered) + 1:]
        if not any("presenting the first frame" in line for line in recovery_log):
            result["pending"].append("Post-recovery first present call")
        if expected_hdr and not any("HDR output presented its first frame" in line for line in recovery_log):
            result["pending"].append("Post-recovery first HDR frame")
        if checks.get("recoveredCapture"):
            result["recovered_capture"] = captures(directory, checks["recoveredCapture"], expected_hdr, recovery_log)
            require(result["recovered_capture"]["sdr"]["original"] != result["initial_capture"]["sdr"]["original"],
                    "Recovery capture reuses the initial capture")
        else:
            result["pending"].append("Fresh world screenshot after device recovery")
    if case == "monitor-transfer":
        require(expected_hdr and run.get("fallbackDisplay") and run["fallbackDisplay"] != result["display"],
                "Missing distinct SDR destination monitor")
        fallback = chain("fallback", False, checks, lifecycle, initial, sdr_view=True)
        restored = chain("recoveredHdr", True, checks, lifecycle, fallback)
        require(initial < fallback < restored, "Monitor transfer timestamps are out of order")
        result["fallback_display"] = run["fallbackDisplay"]
        result["fallback_capture"] = captures(directory, checks["fallbackCapture"], False, game)
        require(result["fallback_capture"]["sdr"]["original"] != result["initial_capture"]["sdr"]["original"],
                "SDR monitor capture reuses the initial capture")
        result["lifecycle_chains"] = {key: checks[key] for key in ("fallback", "recoveredHdr")}
    if case == "display-lifecycle":
        require(expected_hdr and run.get("modeTransitionMonitor") and run.get("displayHdrRestored") is True,
                "Missing Windows HDR toggle or restoration receipt")
        require(not run.get("lifecycleCleanupError"), "PLMDebug cleanup failed")
        fallback = chain("fallback", False, checks, lifecycle, initial, sdr_view=True)
        restored = chain("recoveredHdr", True, checks, lifecycle, fallback)
        resumed = chain("resumeSwapchain", True, checks, lifecycle, restored)
        require(initial < fallback < restored < resumed, "DXGI lifecycle timestamps are out of order")
        suspended = checks["suspended"].splitlines()
        require(f"Package full name is {run['package']}." in suspended and
                "Package state: Suspended" in suspended and "SUCCEEDED" in suspended,
                "Missing successful PLMDebug suspended state for this package")
        result["monitor"] = run["modeTransitionMonitor"]
        result["lifecycle_chains"] = {key: checks[key] for key in ("fallback", "recoveredHdr", "resumeSwapchain")}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("appx", type=Path)
    parser.add_argument("receipt", type=Path, help="Rollback package.json or diagnostic recovery index.json")
    parser.add_argument("run", type=Path, help="Test-HdrGame.ps1 output directory")
    parser.add_argument("--case", choices=("smoke", "device-recovery", "display-lifecycle", "monitor-transfer"),
                        default="smoke")
    parser.add_argument("--expect", choices=("sdr", "hdr"), required=True)
    parser.add_argument("--display", help="Require the recorded Windows display name")
    args = parser.parse_args()
    try:
        receipt = json.loads(args.receipt.read_text(encoding="utf-8-sig"))
        result = verify_run(args.appx, receipt, args.run, args.case, args.expect == "hdr", args.display)
    except (AttributeError, IndexError, KeyError, OSError, TypeError, ValueError, ElementTree.ParseError,
            struct.error, zipfile.BadZipFile, zlib.error) as exc:
        parser.exit(1, f"HDR evidence check failed: {exc}\n")
    result["status"] = "incomplete" if result["pending"] else "verified"
    print(json.dumps(result, indent=2))
    return 2 if result["pending"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
