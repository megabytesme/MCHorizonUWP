"""Platform-side runtime evidence for existing Horizon audit rows.

This module deliberately does not add inventory rows. It updates the existing
setting/feature records with signed November 20/21 receipts and bounded source
limitations; physical evidence is only assigned to the profile that produced it.
"""

from pathlib import Path


BASE = "docs/horizon-uwp-feature-parity-2026-11-20.md"
RUNTIME = "docs/nightly-runtime-completion-2026-11-21.md"
LUMIA = "docs/nightly-lumia-drs-2026-11-21.md"
MODS = "docs/nightly-mod-runtime-2026-11-20.md"
MOBILE = "docs/nightly-mobile-2026-11-20.md"
QA = "deliverables/nightly-2026-11-20/runtime-0.1.17/qa/"

SIGNED_DESKTOP = ("x64-d3d11", "x86-d3d11")
SIGNED_D3D12 = ("x64-d3d12", "arm64-d3d12")
SIGNED_SURFACE = ("arm64-d3d11", "arm64-d3d12")
ACTIVE = ("x64-d3d11", "x86-d3d11", "x64-d3d12", "x64-angle",
          "arm64-d3d11", "arm64-d3d12", "lumia-arm32-d3d11")


def _cell(status, condition, evidence=()):
    return {"status": status, "condition": condition, "evidence": list(evidence)}


def _apply(row, profiles, status, condition, evidence=()):
    cells = row.setdefault("profiles", {})
    for profile in profiles:
        cells[profile] = _cell(status, condition, evidence)


def _refs(product, paths):
    """Only return evidence files that exist in the candidate audit tree."""
    root = Path(product)
    return [path for path in paths if (root / path).is_file()]


def _feature(features, key):
    return next((row for row in features if row.get("key") == key or
                 row.get("id") == "feature." + key), None)


def _setting(settings, key):
    return next((row for row in settings if row.get("key") == key), None)


def _paint_feature(product, features, key, profiles, status, condition, evidence):
    row = _feature(features, key)
    if row is None:
        return
    refs = _refs(product, evidence)
    _apply(row, profiles, status, condition, refs)
    row.setdefault("evidence", []).extend(x for x in refs if x not in row["evidence"])
    row["uwp_runtime_action"] = condition


def apply_platform(settings, features, product, uwp):
    """Paint scoped existing rows with bounded platform runtime/source evidence.

    `product` is the audit product root (for receipt paths); `uwp` is accepted
    by the common extractor API and retained for callers that pass a separate
    UWP source checkout. Evidence added here points only at checked-in receipts.
    """
    refs = lambda *paths: _refs(product, paths)

    # Signed, actual 0.1.17 input evidence: desktop gameplay and Lumia input
    # predate this exact signed build unless an explicit 0.1.17 receipt exists.
    input_rows = {
        "mouse": (SIGNED_DESKTOP, "P", "Signed desktop gameplay exercised ordinary pointer interaction; raw capture, focus loss and sensitivity sweeps lack isolated 0.1.17 receipts.", [RUNTIME]),
        "controller": (ACTIVE, "C", "The prior Lumia GameSir handoff is from an earlier package. Device discovery, mappings, vibration and multi-controller arbitration are not proven for this signed 0.1.17 baseline.", [BASE, MOBILE]),
        "touch": (ACTIVE, "C", "Earlier Lumia package verified menu taps, MOVE and GameSir handoff; inventory, text entry, gestures and exact 0.1.17 routing remain unverified.", [BASE, MOBILE]),
        "gyro": (("lumia-arm32-d3d11",), "P", "Signed Lumia diagnostic verified yaw and pitch in-world. Alternate orientation, disable/re-enable and controller coexistence remain open.", [LUMIA]),
        "ime": (ACTIVE, "C", "No signed receipt establishes Windows touch keyboard/IME composition in chat across the tested packages.", [BASE]),
        "clipboard": (ACTIVE, "C", "The UWP clipboard branch is present, but Unicode copy/paste and startup behavior have no signed 0.1.17 receipt.", [BASE]),
        "accessibility": (ACTIVE, "C", "Shared accessibility controls exist; per-option contrast, scaling, focus and input verification was not performed on the signed candidate.", [BASE]),
        "narrator": (ACTIVE, "S", "UWP narrator backend is inactive; preferences can persist in shared settings but no speech or focus announcement is produced.", [BASE]),
        "xbox-impulse": (ACTIVE, "U", "Xbox impulse output uses optional desktop HID support excluded from the UWP controller feature set.", ["uwp/game-bridge/Cargo.toml"]),
        "split-screen": (ACTIVE, "C", "The host exposes one XAML/CoreWindow. Upstream split-screen options exist, but per-seat rendering and two-controller routing were not exercised.", [BASE]),
    }
    for key, (profiles, status, condition, evidence) in input_rows.items():
        row = _feature(features, key)
        if row is None:
            continue
        checked = refs(*evidence)
        if profiles:
            _apply(row, profiles, status, condition, checked)
        row["uwp_runtime_action"] = condition
        row.setdefault("evidence", []).extend(x for x in checked if x not in row["evidence"])

    # Audio: audio is audible in signed gameplay; device enumeration/switching
    # remains a source-proven port limitation, not a failed speaker test.
    _paint_feature(product, features, "sound-effects", SIGNED_DESKTOP + SIGNED_SURFACE, "P",
                   "Signed gameplay produced non-silent XAudio2 output. Per-category volume, output-device switch and hot-plug behavior remain untested.",
                   [RUNTIME])
    _paint_feature(product, features, "music", ("lumia-arm32-d3d11",), "P",
                   "Earlier Lumia gameplay produced audible music; exact track, volume and 0.1.17 package behavior lack isolated receipts.",
                   [BASE, MOBILE])
    _paint_feature(product, features, "audio-routing", ACTIVE, "U",
                   "UWP XAudio2 opens the default output. The non-CPAL platform settings path does not enumerate or switch audio devices.",
                   [BASE, "uwp/game-bridge/Cargo.toml"])

    # Mod lifecycle: mark only the narrow behavior that receipts prove.
    _paint_feature(product, features, "native-packaged", SIGNED_DESKTOP + ("x64-d3d12", "x64-angle", "arm64-d3d11", "arm64-d3d12"), "P",
                   "Signed GUI fixture rendered native and Wasm markers on the stated routes. This proves package loading and one hook, not arbitrary native APIs or install/update states.",
                   [RUNTIME, MODS])
    _paint_feature(product, features, "wasm-mod", ACTIVE, "P",
                   "Signed fixture rendered a GUI marker; x64 tests exercised deliberate trap and resource limits. Other architectures and mod families remain open.",
                   [RUNTIME, MODS])
    _paint_feature(product, features, "content-mod", ("x64-d3d11",), "P",
                   "Signed content fixture matched client/server registry and joined. Other content families and backend combinations were not covered.",
                   [RUNTIME, MODS])
    _paint_feature(product, features, "mods", SIGNED_DESKTOP, "P",
                   "Signed runtime exercised independent enable state, altered native digest rejection, and Wasm memory/epoch limits. Install, update, remove and restart matrix remains incomplete.",
                   [RUNTIME, MODS])

    # Network, picker, clipboard and lifecycle evidence is intentionally narrow.
    _paint_feature(product, features, "multiplayer", SIGNED_DESKTOP, "P",
                   "Signed 0.1.17 joined the pinned offline server, moved and reconnected. Online authentication, LAN discovery, Realms and general Internet servers are not established by this receipt.",
                   [RUNTIME])
    _paint_feature(product, features, "file-pickers", ACTIVE, "C",
                   "Prior phone receipt imported a standard .hmod through the launcher picker. Worlds, directories, resource packs, report export and current signed package remain unverified.",
                   [BASE, MODS])
    _paint_feature(product, features, "background", ("x64-d3d11", "arm64-d3d12"), "P",
                   "Signed minimize/restore returned to foreground. OS suspend/resume, device loss recovery and repeated resize/rotation are not covered.",
                   [RUNTIME])
    _paint_feature(product, features, "lifecycle", ACTIVE, "C",
                   "Host/window source carries CoreWindow and surface lifetime code, but game-loop integration requires the upstream winit event/window contract; repeated suspend/resume remains untested.",
                   [BASE, "uwp/WINDOWING.md", "uwp/RENDERER_BACKENDS.md"])

    # Set setting-level facts where the existing inventory already has a row.
    setting_actions = {
        "narrator": ("S", ACTIVE, "Narrator preference is stored by shared settings, while the UWP narrator backend is inactive.", [BASE]),
        "narratorHotkey": ("S", ACTIVE, "Hotkey preference does not provide speech while the UWP narrator backend is inactive.", [BASE]),
        "soundDevice": ("U", ACTIVE, "UWP audio opens the default XAudio2 output and has no CPAL device enumeration/switch implementation.", [BASE]),
    }
    for key, (status, profiles, condition, evidence) in setting_actions.items():
        row = _setting(settings, key)
        if row is None:
            continue
        checked = refs(*evidence)
        row["uwp_runtime_action"] = condition
        row["root_cause"] = condition
        row["owner"] = "MCHorizonUWP"
        _apply(row, profiles, status, condition, checked)
        row.setdefault("evidence", []).extend(x for x in checked if x not in row["evidence"])

    # Existing vanilla and extras keys in these families inherit no broad
    # runtime pass from a general gameplay receipt. Keep every row explicit.
    def family(row):
        key = row.get("key", "")
        storage = row.get("storage") or ""
        if key.startswith("key_key.") or key.startswith("bind_key."):
            return ("C", "The binding is parsed and persisted, but per-action routing, conflicts and rebind persistence were not swept on this signed candidate.")
        if key.startswith("bind_pad.") or key in {"keyboard", "radialMode", "splitVibration"}:
            return ("C", "Controller configuration is present; device mapping, dead zones, vibration, handoff and multi-pad routing lack setting-level 0.1.17 evidence.")
        if key.startswith("soundCategory_") or key in {"musicFrequency", "musicToast", "musicOff", "uiSoundReserve"}:
            return ("C", "Signed gameplay had audible audio, but this individual category/track option was not changed and measured in an A/B run.")
        if key.startswith("mods.") or key in {"mods_auto_update", "native_mods_allowed_at"}:
            return ("C", "Runtime receipts cover selected load, enable state and sandbox limits; this individual mod option's lifecycle and restart semantics were not exercised.")
        if key.startswith("extras.") and any(word in key.lower() for word in ("key", "mouse", "split", "zoom", "emote", "sort")):
            return ("C", "Shared extras setting exists, but its input action and saved-value effect were not isolated in the signed 0.1.17 run.")
        if key in {"mouseSensitivity", "mouseWheelSensitivity", "invertXMouse", "invertYMouse", "discrete_mouse_scroll"}:
            return ("C", "Ordinary gameplay interaction was observed, but this input option was not varied against a fixed camera/action measurement.")
        if key in {"highContrast", "highContrastBlockOutline", "chatScale", "debugGuiScale", "onboardAccessibility", "darknessEffectScale", "fovEffectScale", "screenEffectScale"}:
            return ("C", "Shared option is present; no signed per-setting visual/accessibility comparison was recorded.")
        if key in {"fullscreen", "exclusiveFullscreen", "fullscreenResolution", "quitShortcuts", "startedCleanly"}:
            return ("C", "The app uses a single XAML/CoreWindow host. Window mode, close request and suspend/reopen persistence were not individually exercised on this build.")
        if key in {"lastServer", "hideServerAddress", "allowServerListing", "skipMultiplayerWarning", "onlyShowSecureChat", "saveChatDrafts"}:
            return ("C", "The pinned offline server join/reconnect passed; this saved-server, privacy or chat option was not isolated in that test.")
        return None

    for row in settings:
        classification = family(row)
        if classification is None:
            continue
        status, condition = classification
        # The explicit device and package matrix is the scope of this audit.
        _apply(row, ACTIVE, status, condition, refs(BASE, RUNTIME))
        row["uwp_runtime_action"] = condition
