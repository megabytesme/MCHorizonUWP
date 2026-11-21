"""Extract renderer environment inputs and link audited renderer feature profiles."""

import csv
import re
from pathlib import Path

from audit_horizon_core import RENDER, anchor, read, setting_row
from audit_horizon_evidence import PROFILES, cell


RENDER_MATRIX = Path("deliverables/feature-audit/renderer-features.csv")
FEATURE_ROUTES = {
    "renderer-dx11": "renderer-open",
    "renderer-dx12": "renderer-open",
    "renderer-angle": "renderer-open",
    "fixed-scale": "fixed-world-scale",
    "dynamic-scale": "adaptive-world-scale",
    "fxaa": "fxaa",
    "taa": "taa",
    "msaa4x": "terrain-msaa4x",
    "fsr31": "fsr31-frame-generation",
    "hdr": "hdr-output-manual",
    "gpu-culling": "compute-terrain-culling",
    "vsync": "present-vsync-fifo",
    "gpu-timer": "gpu-timing-overlay",
}
SETTING_ROUTES = {
    "graphics.resolutionScale": "fixed-world-scale",
    "graphics.dynamicResolution": "adaptive-world-scale",
    "graphics.targetFps": "adaptive-target-fps",
    "graphics.fxaa": "fxaa",
    "graphics.taa": "taa",
    "graphics.msaa4x": "terrain-msaa4x",
    "graphics.upscaling": "taa-upscaling",
    "graphics.upscalingSharpness": "taa-sharpness",
    "graphics.frameGeneration": "fsr31-frame-generation",
    "graphics.control.HdrCalibrate": "hdr-calibration",
    "graphics.hdrBlackLevel": "hdr-black-level",
    "graphics.hdrPeak": "hdr-peak-brightness",
    "graphics.hdrPaperWhite": "hdr-paper-white",
    "graphics.hdrUiNits": "hdr-ui-brightness",
    "graphics.hdrColorBoost": "hdr-color-boost",
    "graphics.hdrLook": "hdr-look",
    "graphics.gpuCulling": "compute-terrain-culling",
    "graphics.batchTranslucent": "translucent-batching",
    "graphics.presentMode": "present-nonfifo",
    "graphics.screenshotQuality": "supersampled-screenshot",
    "options.txt:maxAnisotropyBit": "max-anisotropy",
}
ENV_READ = re.compile(r'\b(?:std::)?env::(?:var|var_os)\(\s*"([A-Z][A-Z0-9_]+)"\s*\)')


def inventory(official, uwp):
    files = [RENDER / "graphics.rs", RENDER / "gfx.rs"]
    files.extend(path.relative_to(official) for path in (official / RENDER / "gfx").rglob("*.rs")
                 if "tests" not in path.stem and "tests" not in path.parts)
    coverage = {}
    index = {}
    def uses(text):
        constants = dict(re.findall(r'const (w+): &str = "([A-Z][A-Z0-9_]+)"', text))
        found = [(match[1], match.start()) for match in ENV_READ.finditer(text)]
        for match in re.finditer(r'env::(?:var|var_os)(s*(w+)s*)', text):
            if match[1] in constants:
                found.append((constants[match[1]], match.start()))
        return found
    for path in sorted(files):
        source = read(official, path)
        port = read(uwp, path) if (uwp / path).is_file() else ""
        port_uses = dict(uses(port))
        for key, position in uses(source):
            refs = index.setdefault(key, [])
            refs.append(anchor("official", path, source, position))
            if key in port_uses:
                refs.append(anchor("uwp", path, port, port_uses[key]))
            coverage.setdefault(f"official:{path.as_posix()}", []).append(key)
    rows = []
    for key, refs in sorted(index.items()):
        description, values = {
            "HORIZON_GRAPHICS_API": ("Override saved desktop graphics API at launch.", "auto / vulkan / dx12 / opengl"),
            "HORIZON_RAY_TRACING": ("Override pack-controlled ray-query scene policy.", "1 forces on; another value forces off; absent defers to the pack"),
            "HORIZON_MESH_SHADER": ("Requests experimental terrain mesh shading on suitable devices.", "1 enables; absent uses regular path"),
        }.get(key, ("Optional renderer override read at runtime; see cited engine branch.", None))
        row = setting_row("renderer.env", key, "process environment", "unset", values, refs,
                          description, persisted="Launch environment only.")
        row.update(scope="developer", uwp_ui="No normal packaged UI for the environment override.",
                   uwp_runtime_action="Shared renderer environment branch where present; exact AppContainer invocation needs proof.")
        if key == "HORIZON_GRAPHICS_API":
            row["uwp_runtime_action"] = "UWP backend is selected by the host; the desktop override is not a normal launch route."
        rows.append(row)
    return rows, coverage


def load_renderer_matrix(product):
    profiles = {}
    with (product / RENDER_MATRIX).open(encoding="utf-8-sig", newline="") as entries:
        for index, row in enumerate(csv.DictReader(entries), 2):
            profile = row["profile_id"]
            if profile not in PROFILES:
                raise ValueError(f"Renderer matrix has unknown profile {profile}.")
            key = row["feature_id"], profile
            if key in profiles:
                raise ValueError(f"Renderer matrix has duplicate cell {key}.")
            profiles[key] = row
            row["matrix_anchor"] = f"product:{RENDER_MATRIX.as_posix()}:{index}"
    return profiles


def link_renderer(settings, features, product):
    matrix = load_renderer_matrix(product)
    for record in features + settings:
        identifier = record["id"]
        if identifier.startswith("feature."):
            route = FEATURE_ROUTES.get(record["key"])
        else:
            route = SETTING_ROUTES.get(identifier)
        if route is None:
            continue
        renderer = record["key"].removeprefix("renderer-") if route == "renderer-open" else None
        for profile in PROFILES:
            if renderer and renderer not in profile:
                continue
            receipt = matrix.get((route, profile))
            if not receipt:
                continue
            refs = [receipt["matrix_anchor"]]
            refs.extend(part for part in receipt["evidence"].split(" | ") if part)
            status = receipt["status"]
            detail = receipt["port_behavior"]
            existing = record["profiles"].get(profile)
            if existing is None or existing["status"] in {"C", "?"} or identifier.startswith("feature."):
                record["profiles"][profile] = cell(status, detail, refs)
            if status in {"V", "B", "S", "H", "U", "X"}:
                record["evidence"].append(receipt["matrix_anchor"])
    return {f"product:{RENDER_MATRIX.as_posix()}": ["/".join(key) for key in matrix]}
