"""Pinned UWP profile definitions and settings with audited runtime gates."""

from pathlib import Path

from audit_horizon_core import RENDER, anchor, read


PIN = "0a875da91dbb0fdcb7ba8c42c11341aa229af51a"
PRODUCT = "5c26a8060c1cf2dce20ee5707e84699ed0879e5c"
WGPU = "aa7b65c43ef008d0a92d20fcdf9f10fa0883f157"
EVID = "deliverables/nightly-2026-11-20/"
QA = EVID + "runtime-0.1.17/qa/"
RUNTIME = "docs/nightly-runtime-completion-2026-11-21.md"
LUMIA = "docs/nightly-lumia-drs-2026-11-21.md"
DRS = "docs/nightly-drs-2026-11-20.md"
SHADER = "deliverables/feature-audit/shader-evidence/"

PROFILES = {
    "x64-d3d12": "Ben-Desktop RTX 3090 Ti, signed 0.1.17 D3D12",
    "arm64-d3d12": "Surface Pro X Adreno 680, signed 0.1.14 tested; 0.1.17 staged",
    "x86-d3d12": "x86 launcher probe presented through D3D12 on RTX 3090 Ti; signed game run unverified",
    "lumia-arm32-d3d12": "Lumia Arm32, earlier alternative-backend probe did not admit an adapter",
    "x64-d3d11": "Ben-Desktop RTX 3090 Ti, negotiated FL11_1 with compatibility world path",
    "x86-d3d11": "Ben-Desktop x86, negotiated FL11_1 with compatibility world path",
    "arm64-d3d11": "Surface Pro X Adreno 680, negotiated FL11_1 with compatibility world path",
    "lumia-arm32-d3d11": "Lumia 950 XL Adreno 430, negotiated FL11_1 with compatibility world path",
    "desktop-d3d11-fl91": "Ben-Desktop x64 deliberately negotiated FL9_1; scaler probes only",
    "x86-desktop-d3d11-fl91": "Ben-Desktop x86 deliberately negotiated FL9_1; scaler probes only",
    "arm64-surface-d3d11-fl91": "Surface Pro X signed AppContainer explicitly requested FL9_1; scaler probes only",
    "lumia-arm32-d3d11-fl91": "Lumia signed AppContainer explicitly requested FL9_1; scaler probes only",
    "x64-angle": "Ben-Desktop x64, signed ANGLE 0.1.17 hardware GLES 3.0 through D3D11",
    "x86-angle": "x86 signed 0.1.17 ANGLE EGL instance reached a surface-creation failure on Ben-Desktop",
    "arm64-angle": "Earlier ARM64 native ANGLE probe presented GLES 3.0; signed November 20 game route unverified",
    "lumia-arm32-angle": "Lumia ARM32 EGL surface creation failed on the recorded November 20 probe",
}

DX11 = tuple(profile for profile in PROFILES if "d3d11" in profile)
REAL_DX11 = ("x64-d3d11", "x86-d3d11", "arm64-d3d11", "lumia-arm32-d3d11")
DX12 = ("x64-d3d12", "arm64-d3d12", "x86-d3d12")
ANGLE = ("x64-angle", "x86-angle", "arm64-angle")
ACTIVE = REAL_DX11 + ("x64-d3d12", "arm64-d3d12", "x64-angle")

STATUS = {
    "V": "Functionally verified in stated physical scope",
    "P": "Partial implementation or partial functional evidence",
    "B": "Broken in a demonstrated case",
    "S": "Stub or no effective runtime action",
    "H": "Intentionally hidden or gated",
    "U": "Missing from the port",
    "X": "Demonstrated OS or hardware lack in stated scope",
    "C": "Source only or runtime behavior unverified",
    "?": "Unknown",
}

OWNERS = {
    "Horizon": "Official shared client, settings and renderer",
    "MCHorizonUWP": "AppContainer bridge, integration patch, packaging and QA",
    "wgpu": "Pinned D3D11 HAL, public capabilities and shader translation",
    "HDR": "UWP window/display-state bridge and HDR presentation",
    "ANGLE": "ANGLE and wgpu GLES device, limits and EGL surface",
}


def cell(status, condition, evidence=()):
    return {"status": status, "condition": condition, "evidence": list(evidence)}


def apply_cells(row, profiles, status, condition, evidence=()):
    for profile in profiles:
        row["profiles"][profile] = cell(status, condition, evidence)


def source_refs(uwp, file, patterns):
    path = RENDER / file
    source = read(uwp, path)
    return [anchor("uwp", path, source, source.index(pattern)) for pattern in patterns]


def audit_setting(row, uwp):
    key = row["key"]
    if row["id"].startswith("graphics."):
        graphics_evidence(row, uwp)
    elif key in {"menuBackgroundBlurriness", "menuBlur", "maxAnisotropyBit", "textureFiltering"}:
        if key.startswith("menu"):
            action = "Menu blur requires the source copy path, which the compatibility and GLES surfaces lack."
            gates = DX11 + ANGLE
        elif key == "maxAnisotropyBit":
            action = "Compatibility atlas sampler sets anisotropy to 1."
            gates = DX11
        else:
            apply_cells(row, DX11, "P", "Compatibility atlas uses a separate fixed sampler; the appearance of each filtering mode needs comparison.")
            return
        needle = "let menu_blur = frame.menu_blur.filter" if key.startswith("menu") else "anisotropy_clamp: 1"
        refs = source_refs(uwp, "gfx.rs", [needle])
        apply_cells(row, gates, "S", action, refs)
        row.update(uwp_runtime_action=action, root_cause=action, owner="MCHorizonUWP")
        row["sources"].extend(refs)
    elif key == "improvedTransparency":
        reason = "Improved transparency requires FLOAT32_BLENDABLE; the compatibility device request and x64 ANGLE runtime lack it."
        refs = source_refs(uwp, "gfx/oit.rs", ["FLOAT32_BLENDABLE"])
        apply_cells(row, DX11 + ("x64-angle",), "S", reason, [*refs, QA + "x64-angle/game.log"])
        row.update(uwp_runtime_action=reason, root_cause=reason, owner="MCHorizonUWP")
        row["sources"].extend(refs)
    elif key in {"fullscreenResolution", "exclusiveFullscreen"}:
        action = "In-game settings are shared with Windows; SwapChainPanel fullscreen is controlled by the XAML host. Exclusive display mode needs its own path and receipt."
        apply_cells(row, ACTIVE, "P", action, [RUNTIME])
        row.update(uwp_runtime_action=action, root_cause="The game is hosted in a XAML SwapChainPanel.",
                   owner="MCHorizonUWP")


def graphics_evidence(row, uwp):
    key = row["key"]
    row.setdefault("owner", "Horizon")
    where = row["profiles"]
    if key == "resolutionScale":
        row.update(
            uwp_runtime_action="Renders world color/depth at the scale and composites native-size HUD; uses a dedicated compatibility scaler.",
            test_method="At matched camera and output size, log color/depth and output dimensions, compare HUD pixels, then resize and repeat.",
            enabling_conditions="Scale 50–200% on disk; adaptive mode uses the scale as a floor below 100%.",
            owner="MCHorizonUWP",
        )
        verified = {
            "x64-d3d11": QA + "x64/result.json",
            "x86-d3d11": QA + "x86/result.json",
            "arm64-d3d11": EVID + "runtime-evidence/arm64/dx11-world-67-interactive/result.json",
            "lumia-arm32-d3d11": LUMIA,
        }
        for profile, receipt in verified.items():
            where[profile] = cell("V", "Signed saved-world scaling verified at the measured values.", [receipt])
        apply_cells(row, tuple(p for p in DX11 if p.endswith("-fl91")), "P",
                    "Forced FL9_1 scaler readback verified separately; full game verification remains open.", [DRS])
        row["evidence"].extend(verified.values())
        row["sources"].extend(source_refs(uwp, "gfx/fl91_world_scale.rs", ["pub(super) struct WorldScale"]))
        return
    if key in {"dynamicResolution", "targetFps"}:
        action = "Controller lowers or raises world dimensions; the timing source is validated GPU timestamps or frame pressure."
        row.update(uwp_runtime_action=action,
                   enabling_conditions="Enable Dynamic Resolution; choose a scale below 100% and a repeatable load.",
                   owner="MCHorizonUWP", test_method="Capture sustained scale trajectory, timing source, frame intervals, floor and recovery.")
        where["lumia-arm32-d3d11"] = cell("V" if key == "dynamicResolution" else "P",
            "0.1.16 moved from 50% to 55% with frame pressure and the target set to 30 FPS.", [LUMIA])
        where["arm64-d3d12"] = cell("P", "0.1.14 run had invalid GPU timestamps; 0.1.17 source fix needs the final signed physical rerun.", [RUNTIME])
        angle_evidence = QA + "x64-angle/game.log"
        refs = source_refs(uwp, "gfx.rs", ["pub fn world_scale_uses_frame_pressure"])
        where["x64-angle"] = cell("S", "The tested device enabled no GPU timer and the compatibility-only no-timer fallback does not apply.", [angle_evidence, *refs])
        row["sources"].extend(refs)
        row["evidence"].extend([LUMIA, RUNTIME, angle_evidence])
        return
    if key == "frameGeneration":
        reason = "Compatibility renderer forces frame generation off."
        refs = source_refs(uwp, "gfx.rs", ["pub fn set_frame_generation", "&& frame.menu_blur.is_none()"])
        apply_cells(row, DX11, "S", reason, refs)
        apply_cells(row, ANGLE, "S", "GLES surface cannot accept the required generated-frame COPY_DST blit; observed x64 GLES 3.0 lacks compute.")
        apply_cells(row, DX12, "C", "Requires surface COPY_DST, compute, compatible frame path and pacing; interpolate and present a generated frame.")
        row.update(
            uwp_ui="Frame Generation remains in Graphics / Quality; availability and the renderer gate differ.",
            uwp_runtime_action="Modern path may use motion/optical-flow interpolation and a second presentation. Compatibility path bypasses it.",
            root_cause=reason + " ANGLE has independent GLES surface and compute limitations.",
            owner="MCHorizonUWP",
            enabling_conditions="Onscreen COPY_DST, compatible output/HUD and compute; normal world frame without menu blur or incompatible pack post.",
            test_method="Capture presentation timestamps and label generated frames. Verify Off, 2× interpolation, cadence, resize and HDR/pack interactions.",
        )
        row["sources"].extend(refs)
        return
    if key in {"taa", "fxaa", "msaa4x", "upscaling", "upscalingSharpness"}:
        if key == "msaa4x":
            refs = source_refs(uwp, "gfx/msaa.rs", ["Backend::Dx11"])
            apply_cells(row, DX11 + ANGLE, "H", "Renderer gates 4× terrain MSAA by backend and format.", refs)
            apply_cells(row, DX12, "C", "Exercise supported terrain format, actual four samples and resolve.")
            row["uwp_ui"] = "4× choice offered only when output reports MSAA eligibility."
            reason = "Device/backend/terrain sample and resolve gates."
        else:
            refs = source_refs(uwp, "gfx.rs", ["pub fn set_world_post"])
            status = "S"
            reason = ("Compatibility scaler has its own blit and ignores TAA upscaling and sharpness."
                      if key.startswith("upscaling") else "Compatibility path forces the post antialiasing setting off.")
            apply_cells(row, DX11, status, reason, refs)
        row.update(uwp_runtime_action=reason, root_cause=reason, owner="MCHorizonUWP")
        row["sources"].extend(refs)
        return
    if key.startswith("hdr") or key == "HdrCalibrate":
        if key in {"hdrLighting", "hdrUiBrightness"}:
            return
        if key == "hdrFailedStart":
            row["uwp_runtime_action"] = "Holds HDR off after an incomplete startup on the same driver."
            return
        reason = "D3D11 compatibility and GLES do not expose the required presentation route."
        refs = source_refs(uwp, "gfx.rs", ["let hdr_output = if fl91"])
        apply_cells(row, DX11 + ANGLE, "H", reason, refs)
        apply_cells(row, DX12, "P" if key == "hdr" else "C",
                    "Manual On and calibration require measured output; WinRT panel lacks the desktop OS HDR state that Auto uses.",
                    ["wgpu:wgpu-hal/src/dx12/instance.rs:171",
                     "wgpu:wgpu-hal/src/dx12/adapter.rs:1398", RUNTIME])
        row.update(uwp_runtime_action="Graphics/output consumes HDR policy when a usable HDR frame is offered.",
                   uwp_ui="Visible as a supported or disabled HDR control according to EffectiveOutput.",
                   enabling_conditions="Active compatible display/output and HDR frame; look and HUD controls also depend on pack/compositor.",
                   root_cause=reason + " DX12 Auto has no HDR display state in the WinRT panel path.",
                   test_method="Record OS HDR state, accepted format/color space, measured nits and visual change under Auto and manual On.",
                   owner="HDR")
        if key == "hdr":
            row["mode_findings"] = {"Off": "SDR path available", "Auto": "WinRT D3D12 display-state query has no data",
                                    "On": "D3D12 format advertised; measured output unverified"}
        row["sources"].extend(refs)
        return
    if key in {"gpuCulling", "batchTranslucent"}:
        condition = "Requires GPU capability and renderer route: counted indirect/base-vertex semantics for GPU culling; indirect draws for batching."
        apply_cells(row, DX11 + ANGLE, "H", condition)
        if key == "gpuCulling":
            refs = source_refs(uwp, "gfx/gpu_culling.rs", ["vertex_index"])
            apply_cells(row, DX12, "H", "Normal DX12 flat-page terrain lacks the vertex-index base contract for counted draws; other layouts require a separate test.", refs)
            row["sources"].extend(refs)
        row.update(uwp_runtime_action=condition, enabling_conditions=condition, root_cause=condition,
                   owner="wgpu")
        return
    if key == "pickedShaderPack":
        apply_cells(row, DX11, "H", "Picker/config can retain selection; compatibility renderer does not admit pack drawing.")
        where["x64-d3d11"] = cell("H", "Signed Golden Hour rejection produced a base-world frame.",
                                [SHADER + "dx11-golden/result.json"])
        where["x64-d3d12"] = cell("P", "Two native packs and a classic pack reached signed world frames. Option apply and stage isolation remain open.",
                                [SHADER + case + "/result.json" for case in ("dx12-golden", "dx12-hdr", "dx12-classic")])
        where["x64-angle"] = cell("B", "Golden Hour native sky layout and classic entity storage validation failed on the tested GLES 3.0 context.",
                                [SHADER + "gl-golden/game.log", SHADER + "gl-classic/game.log"])
        row.update(uwp_runtime_action="Shared picker and loader. Execution is stage- and backend-specific.",
                   root_cause="The FL9_1 projection skips pack submission; modern backends need separate per-stage proof.",
                   owner="MCHorizonUWP", test_method="Use shader-pack-tests.csv fixtures and capture per-stage frame changes.")
        return
    if key == "graphicsApi":
        row.update(uwp_ui="UWP selects its backend in the pregame XAML shell.",
                   uwp_runtime_action="Host owns the compiled and probed backend; saved in-game API is not equivalent to backend selection.",
                   root_cause="Entry point creates a host-selected UWP renderer.", owner="MCHorizonUWP")
        apply_cells(row, ACTIVE, "P", row["uwp_runtime_action"], ["uwp/RENDERER_BACKENDS.md"])


FEATURE_OVERRIDES = {
    "shader-pack-native": (
        ("x64-d3d12",), "P",
        "Golden Hour and Horizon HDR loaded and rendered signed SDR game frames. Per-stage execution and option coverage remain open.",
        [SHADER + "dx12-golden/result.json", SHADER + "dx12-hdr/result.json"],
    ),
    "shader-pack-classic": (
        ("x64-d3d12",), "P",
        "Signed classic fixture rendered a visible depth-based composite with both GUI markers. Other classic stages require individual proof.",
        [SHADER + "dx12-classic/result.json", SHADER + "composite-pixel-check.json"],
    ),
    "world-entry": (
        ("x64-d3d11", "x86-d3d11", "x64-d3d12", "x64-angle"), "V",
        "Signed 0.1.17 reached a saved world, rendered a game PNG and drew both GUI probes.",
        [QA + case + "/result.json" for case in ("x64", "x86", "x64-dx12", "x64-angle")],
    ),
    "multiplayer": (("x64-d3d11", "x86-d3d11"), "P",
                    "Signed offline pinned-server join, movement and reconnect confirmed. Online auth and LAN discovery need their own exercise.", [RUNTIME]),
    "screenshots": (("x64-d3d11", "x86-d3d11", "x64-d3d12", "x64-angle", "lumia-arm32-d3d11"), "P",
                    "0.1.17 game-generated PNGs observed; F2, quality modes and verified save collision scope differ.", [RUNTIME, LUMIA]),
    "gyro": (("lumia-arm32-d3d11",), "P",
             "Physical yaw and pitch observed. Opposite orientation, disable/re-enable, and numerical trace need follow-up.", [LUMIA]),
    "saves": (("x64-d3d11",), "P",
              "Signed diagnostic saved and quit to title; a new launcher PID reopened the same save and both hooks.", [RUNTIME]),
    "music": (("lumia-arm32-d3d11",), "P", "Physical user heard the game over a Lumia diagnostic; isolate each music and volume control.", [LUMIA]),
    "sound-effects": (("x64-d3d11", "x86-d3d11", "x64-d3d12", "arm64-d3d11", "arm64-d3d12"), "P",
                      "Non-silent XAudio2 observed in a saved world; no per-category or device-switch proof.", [RUNTIME]),
    "native-packaged": (("x64-d3d11", "x86-d3d11", "x64-d3d12", "x64-angle", "arm64-d3d11", "arm64-d3d12"), "P",
                        "Signed GUI fixture rendered markers. Additional native APIs and installation states require individual proof.", [RUNTIME]),
    "wasm-mod": (ACTIVE, "P", "Signed fixture drew a GUI marker; separate x64 runs exercised trap and resource limits.", [RUNTIME]),
    "content-mod": (("x64-d3d11",), "P", "Shared content fixture matched server/client registry and joined. Feature-family coverage remains open.", [RUNTIME]),
    "background": (("x64-d3d11", "arm64-d3d12"), "P", "Signed minimization/restore proved foreground recovery; suspend/resume lifecycle needs an independent test.", [RUNTIME]),
    "narrator": (ACTIVE, "S", "Narrator preferences remain available in shared code; the UWP narrator backend is inactive.", ["docs/horizon-uwp-feature-parity-2026-11-20.md"]),
    "audio-routing": (ACTIVE, "U", "UWP XAudio2 opens default output and lacks CPAL device enumeration and switching.", ["docs/horizon-uwp-feature-parity-2026-11-20.md"]),
    "play-with-friends": (ACTIVE, "U", "The UWP bridge omits the desktop optional tunnel feature.", ["uwp/game-bridge/Cargo.toml"]),
}
