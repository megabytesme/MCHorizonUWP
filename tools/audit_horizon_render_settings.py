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

ENVIRONMENT = {
    "HORIZON_DEVICE_OPEN_TIMEOUT": ("Changes the deadline for instance, adapter and device selection.", "positive whole seconds", "30 seconds"),
    "HORIZON_FLAT_DRAWS": ("Forces the flat terrain draw layout or disables it where the path permits.", "0 disables / 1 requests; requires route capability", "backend policy"),
    "HORIZON_FRAMEGEN_DEBUG": ("Displays generated-frame or motion diagnostics rather than ordinary interpolation output.", "generated / motion", "unset"),
    "HORIZON_GPU_CULL": ("Changes GPU occlusion policy separately from the counted-draw UI option.", "0 disables / all includes nearby sections", "adapter policy"),
    "HORIZON_GRAPHICS_API": ("Overrides the saved desktop graphics API for this launch.", "auto / vulkan / dx12 / opengl", "saved API selection"),
    "HORIZON_HDR_DEBUG": ("Tints expanded HDR pixels magenta and greys the remainder.", "1 enables", "off"),
    "HORIZON_HDR_DISPLAY": ("Feeds a synthetic HDR state to the offscreen display probe.", "path to a file containing on or off", "no display report"),
    "HORIZON_HDR_OUTPUT": ("Restricts the advertised HDR output to one encoding. Offscreen uses the named encoding.", "pq / scrgb", "onscreen auto; offscreen no HDR offer"),
    "HORIZON_MEASURE_SECTION_MARKS": ("Opt-in test for scene-section marker measurements.", "1 runs the measurement", "off"),
    "HORIZON_MESH_SHADER": ("Requests experimental terrain mesh shading on capable hardware.", "1 requests", "regular path"),
    "HORIZON_NO_ENTITY_SKIN": ("Disables the GPU entity skinning path.", "any present value", "device policy"),
    "HORIZON_PACKED_QUADS": ("Controls packed quad processing where the renderer supports it.", "0 disables", "enabled where supported"),
    "HORIZON_RAY_TRACING": ("Overrides the native pack's ray query scene policy.", "1 forces on; another value forces off", "pack policy"),
    "HORIZON_RENDER_CAPTURE": ("Requests renderer capture-copy support.", "presence enables the request", "unset"),
    "HORIZON_RESOURCES": ("Selects prepared resource data for the cited shader/line tests.", "prepared resource directory", "test fallback path"),
    "HORIZON_RT_CHUNKS": ("Overrides the terrain reach of the ray query scene.", "integer chunk reach", "8 chunks"),
    "HORIZON_SHADER_PACK_DUMP": ("Requests readback of a named classic pack texture.", "pack texture name", "off"),
    "HORIZON_SHADER_PACK_SKIP": ("Skips listed stages in a classic shader pack.", "comma-separated program names", "empty"),
    "HORIZON_SHADER_PACK_WITHOUT": ("Masks named shader-pack capabilities to exercise fallbacks.", "comma-separated capability names", "empty"),
    "HORIZON_SPRITE_SPLIT": ("Requests smaller sprite texture arrays to compare texture splitting.", "1 through SPRITE_SPLIT", "automatic"),
    "HORIZON_SUBMIT_THREAD": ("Switches the optional render submission worker.", "0 disables / 1 enables", "disabled on Windows"),
    "HORIZON_TERRAIN_LOOKS": ("Requests the terrain looks shader path on a compatible adapter.", "1 requests", "off"),
    "HORIZON_VISBUFFER": ("Requests a visibility buffer when storage binding limits allow.", "1 requests", "off"),
    "WGPU_ALLOW_UNDERLYING_NONCOMPLIANT_ADAPTER": ("Controls whether wgpu exposes underlying noncompliant adapters. The cited source uses it in a test guard.", "wgpu backend setting", "unset"),
    "WGPU_BACKEND": ("Supplies explicit wgpu backend selection and affects the client's adapter search.", "backend names accepted by the pinned wgpu instance", "application backend policy"),
    "WGPU_DX12_COMPILER": ("Signals an explicit wgpu DX12 compiler choice and bypasses the default choice logic.", "compiler name from the pinned DX12 option", "automatic"),
    "WGPU_VALIDATION_INDIRECT_CALL": ("Changes wgpu indirect-call checking. The cited source uses it in a test guard.", "wgpu backend setting", "unset"),
}
TEST_ENVIRONMENT = {
    "HORIZON_MEASURE_SECTION_MARKS", "HORIZON_RESOURCES",
    "WGPU_ALLOW_UNDERLYING_NONCOMPLIANT_ADAPTER", "WGPU_VALIDATION_INDIRECT_CALL",
}

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
        description, values, initial = ENVIRONMENT.get(
            key, ("Optional override observed in the cited source.", None, "unset"),
        )
        runner_only = key in TEST_ENVIRONMENT
        storage = "test environment" if runner_only else "process environment"
        row = setting_row("renderer.env", key, storage, initial, values, refs,
                          description, persisted="Launch environment only.")
        row.update(scope="developer", uwp_ui="This environment input has no normal packaged UI.",
                   uwp_runtime_action=("Diagnostic harness input in the cited source." if runner_only else
                                       "Environment-sensitive source branch. Verify the exact AppContainer route."))
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
