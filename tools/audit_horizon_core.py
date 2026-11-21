"""Extract Horizon graphics and extras declarations at the audited source pin."""

import re
from pathlib import Path


RENDER = Path("crates/horizon-client-render/src")
CONDITION = "Use a supported device and exercise the setting in its relevant scene."
GRAPHICS_UI = {
    "blockEntityCacheShulkerBoxes": "CacheShulkerBoxes",
    "blockEntityCacheSigns": "CacheSigns",
    "blockEntityCacheDecoratedPots": "CacheDecoratedPots",
    "blockEntityCacheBells": "CacheBells",
    "blockEntityCacheChests": "CacheChests",
    "blockEntityCacheBanners": "CacheBanners",
    "blockEntityCacheCopperGolemStatues": "CacheCopperGolemStatues",
    "uploadBudgetKiB": "UploadBudget",
    "framePacing": "Pacing",
    "fxaa": "Antialiasing",
    "taa": "Antialiasing",
    "msaa4x": "Antialiasing",
    "hdrUiNits": "HdrUiBrightness",
    "pickedShaderPack": "ShaderPacks",
    "batchTranslucent": "TranslucentBatching",
}
GRAPHICS_FIELDS = {
    "HdrCalibrate": "action; opens calibration or restores automatic values",
}
GRAPHICS_LABELS = {
    "HdrLook": "Look",
    "HdrPaperWhite": "Paper White",
    "HdrPeak": "Peak Brightness",
    "HdrUiBrightness": "UI Brightness",
    "HdrCalibrate": "Calibrate… / Calibrate Anyway… / Use Automatic Setup",
}
GRAPHICS_SPECIAL = {
    "blockEntityCache": (
        "legacy alias for every blockEntityCache* model switch",
        "Ignored unless supplied; legacy umbrella boolean",
    ),
    "hdrLighting": ("legacy alias for hdrLook", "true → Horizon; false → Vanilla"),
    "hdrUiBrightness": (
        "legacy percentage of Paper White; hdrUiNits takes precedence",
        "25–100% for conversion; 100 is Auto",
    ),
    "hdrFailedStart": ("failed HDR start display hold marker", "driver identifier or empty marker"),
}
EXTRA_UI = {
    "wheelCyclesListSettings": "WheelCyclesSettings",
    "zoomKey": "Zoom",
    "zoomLevel": "Zoom",
    "zoomSmooth": "Zoom",
    "zoomSensitivity": "Zoom",
    "lodZoomDetail": "Zoom",
    "fpsCorner": "FpsCounter",
    "fpsLows": "FpsCounter",
    "keystrokesCorner": "Keystrokes",
    "keystrokesCps": "Keystrokes",
    "minimapCorner": "Minimap",
    "minimapSize": "Minimap",
    "minimapShape": "Minimap",
    "minimapZoom": "Minimap",
    "minimapRotate": "Minimap",
}
EXTRA_LABELS = {
    "zoomKey": "Zoom Key",
    "zoomLevel": "Zoom Level",
    "zoomSmooth": "Smooth Zoom",
    "zoomSensitivity": "Zoom Sensitivity",
    "lodZoomDetail": "LOD Zoom Detail",
    "fpsCorner": "FPS Counter position",
    "fpsLows": "FPS Counter lows",
    "keystrokesCorner": "Keystrokes position",
    "keystrokesCps": "Keystrokes CPS",
    "minimapCorner": "Minimap position",
    "minimapSize": "Minimap size",
    "minimapShape": "Minimap shape",
    "minimapZoom": "Minimap zoom",
    "minimapRotate": "Minimap rotation",
    "mouseTweaksWheel": "Wheel Tweak",
    "sortKey": "Sort Inventory key",
}


def read(root, relative):
    return (root / relative).read_text(encoding="utf-8")


def anchor(repo, relative, source, position):
    return f"{repo}:{relative.as_posix()}:{source.count(chr(10), 0, position) + 1}"


def section(source, begin, end):
    start = source.index(begin)
    return source[start:source.index(end, start + len(begin))], start


def literal_arms(source, start, end, constants=None, indent=16):
    body, offset = section(source, start, end)
    arms = list(re.finditer(
        r'(?m)^' + " " * indent + r'(?:"([^"]+)"|([A-Za-z_][A-Za-z0-9_:]*))'
        r'(?: if [^\n]+?)? =>\s*', body,
    ))
    for index, arm in enumerate(arms):
        name = arm[1] or (constants or {}).get(arm[2])
        if name is None:
            continue
        stop = arms[index + 1].start() if index + 1 < len(arms) else len(body)
        yield name, body[arm.end():stop].strip().rstrip(","), offset + arm.start()


def default_fields(source, start, end):
    body, _ = section(source, start, end)
    return {
        key: value for key, value in re.findall(r'(?m)^\s*(\w+): ([^,\n]+),$', body)
        if key not in {"pub", "Self"}
    }


def locations(source, kind):
    body, offset = section(source, "pub fn rows()", "fn ")
    pattern = rf"Page::(\w+)\s*,|[veh]\(\s*(?:crate::extras::)?{kind}::(\w+)\s*,"
    pages = {}
    for item in re.finditer(pattern, body):
        if item[1]:
            page = item[1]
        else:
            description = re.match(r'\s*\w+\s*,\s*"([^"]+)"', body[item.end():])
            pages[item[2]] = (page, description[1] if description else None, offset + item.start())
    return pages


def setting_row(namespace, key, storage, default, values, sources, description, label=None,
                page=None, persisted="Recorded by the configuration writer."):
    return {
        "id": f"{namespace}.{key}",
        "key": key,
        "aliases": [],
        "scope": "settings",
        "storage": storage,
        "official_ui_label": label,
        "official_ui_page": page,
        "values": values,
        "default": default,
        "official_baseline_status": "C",
        "official_windows_behavior": description,
        "expected_visible_effect": description,
        "uwp_ui": ("Shared control; recheck backend and state gates." if page
                   else "No direct standalone control identified."),
        "uwp_persistence": persisted,
        "uwp_runtime_action": None,
        "profiles": {},
        "enabling_conditions": CONDITION,
        "sources": sources,
        "evidence": [],
        "test_method": ("Compare default and exercised values on a fixed fixture, observe the effect, "
                        "and reopen the same profile to check persistence."),
        "root_cause": None,
        "recommendation": "Capture a focused settings A/B on the relevant signed profile.",
    }


def rule(arm, default):
    if "flag(" in arm or default in ("true", "false"):
        return "true / false"
    if clamp := re.search(r"\.clamp\(([^,\n]+),\s*([^\n)]+)\)", arm):
        return f"{clamp[1]}–{clamp[2]}"
    if cap := re.search(r"number\(.*?\)\.min\(([\w:]+)\)", arm):
        return f"0–{cap[1]}"
    return None


def declaration(source, field):
    match = re.search(rf"(?m)^(?P<docs>(?:    ///[^\n]*\n)*)    pub {re.escape(field)}: ", source)
    if not match:
        return "", None
    return " ".join(line.removeprefix("    ///").strip() for line in match["docs"].splitlines()), match.start()


def graphic_values(source, arm, default):
    if values := rule(arm, default):
        return values
    if enum := re.search(r"(\w+)::(?:parse|from_name)\(value", arm):
        name = enum[1]
        body = re.search(rf"impl {name} \{{([\s\S]*?)\n\}}", source)
        if body:
            storage = re.search(r'pub fn (?:key|name)\(self\)[^\n]+\{\n(.*?)^    \}', body[1],
                                flags=re.DOTALL | re.MULTILINE)
            variants = re.findall(r'\w+::\w+ => "([^"]+)"', storage[1]) if storage else []
            if variants:
                return " / ".join(dict.fromkeys(variants))
    return None


def graphics(official, uwp):
    path, ui_path, map_path = (RENDER / f for f in ("graphics.rs", "graphics_ui.rs", "bluemap.rs"))
    source, ui, bluemap = (read(official, f) for f in (path, ui_path, map_path))
    patched, patched_ui = read(uwp, path), read(uwp, ui_path)
    ui_items = locations(ui, "H")
    uwp_items = locations(patched_ui, "H")
    patched_keys = {key: pos for key, _, pos in literal_arms(
        patched, "match key.trim() {", "pub fn serialize(")}
    defaults = default_fields(source, "impl Default for Graphics", "impl Graphics")
    label_body, _ = section(ui, "fn label(self, s: &Settings, out: &EffectiveOutput)", "fn set_slider")
    labels = dict(re.findall(
        r'H::(\w+)\s*=>\s*\{?\s*(?:labelled(?:_percent)?|Text::literal\(lang::get)\(\s*"([^"]+)"',
        label_body,
    ))
    labels.update(GRAPHICS_LABELS)
    labels["LodDistance"] = "LOD Distance"
    labels["ChunkUpdates"] = "Chunk Updates"
    labels["UploadBudget"] = "Upload Budget"
    labels["Workers"] = "Worker Threads"
    labels["Hdr"] = "HDR"
    labels["ShaderPacks"] = "Shader Packs..."
    labels["TranslucentBatching"] = "Translucent Batching"
    rows = []
    coverage = {f"official:{path.as_posix()}": [], f"official:{ui_path.as_posix()}": []}
    for key, arm, pos in literal_arms(source, "match key.trim() {", "pub fn serialize("):
        # Earlier aliases have their own parse paths and can affect multiple stored values.
        if not re.fullmatch(r"[a-zA-Z][a-zA-Z0-9]*", key):
            continue
        ctl = GRAPHICS_UI.get(key, key[0].upper() + key[1:])
        ui_entry = ui_items.get(ctl)
        field_match = re.search(r"g\.(\w+)(?:\.\w+)?\s*=", arm)
        field = field_match[1] if field_match else None
        effect, decl = declaration(source, field) if field else ("", None)
        default = defaults.get(field)
        values = graphic_values(source, arm, default)
        if key == "blueMapRadius":
            default, values = "64 chunks", "0–4096 chunks in file; accepts blocks or chunks"
        elif key == "blueMapAutoDetect":
            default, values = "false", "true / false"
        elif key == "hdrLook":
            default, values = "Horizon", "horizon / vanilla"
        elif key == "hdrUiNits":
            default, values = "0 (Auto)", "0 (Auto) or 10–480 nits in 10-nit steps"
        elif key == "hdrPaperWhite":
            values = "0 (Auto) or 10–480 nits in 10-nit steps"
        elif key == "hdrPeak":
            values = "0 (Auto) or 200–10,000 nits"
        elif key == "hdrBlackLevel":
            values = "0–100 hundredths of a nit"
        elif key in {"hdr", "gpuCulling"}:
            values = "false (off) / true (on) / auto"
        elif key == "presentMode":
            values = "auto / fifo / immediate / mailbox (where exposed)"
        elif key == "framePacing":
            values = "efficient / precise"
        elif key == "overlay":
            values = "off / fps / graph"
        elif key == "screenshotQuality":
            values = re.search(r"SCREENSHOT_QUALITIES: [^=]+=\s*([^;]+);", source)[1]
        elif key == "pickedShaderPack":
            values = "pack file/directory name or empty string (Off)"
        elif key == "lodDistance":
            values = "0 (Off) to 4,194,304 on disk; integrated GPU effective cap 128"
        if key in GRAPHICS_SPECIAL:
            effect, values = GRAPHICS_SPECIAL[key]
            default = "unset"
            ui_entry = None
        elif key in {"fxaa", "taa", "msaa4x"}:
            effect = {
                "fxaa": "Smooths edges in a single post-processing pass.",
                "taa": "Uses frame history to smooth edges and reduce motion shimmer.",
                "msaa4x": "Multisamples terrain geometry; excludes FXAA, TAA and TAA upscaling.",
            }[key]
        effect = effect or (ui_entry[1] if ui_entry else None)
        row = setting_row(
            "graphics", key, "horizon-graphics.txt", default, values,
            [anchor("official", path, source, pos)],
            effect, labels.get(ctl) if ui_entry else None,
            f"Graphics / {ui_entry[0]}" if ui_entry else None,
        )
        if decl is not None:
            row["sources"].append(anchor("official", path, source, decl))
        if key in patched_keys:
            row["sources"].append(anchor("uwp", path, patched, patched_keys[key]))
        else:
            row["uwp_persistence"] = "No matching parse declaration found."
        if ui_entry and ctl not in uwp_items:
            row["uwp_ui"] = "Official control has no matching row in the patched source."
        if ctl in uwp_items and ui_entry:
            row["sources"].append(anchor("uwp", ui_path, patched_ui, uwp_items[ctl][2]))
        if ui_entry:
            row["sources"].append(anchor("official", ui_path, ui, ui_entry[2]))
        else:
            row["scope"] = "hidden"
        if key == "hdrUiNits":
            row["aliases"] = ["hdrUiBrightness"]
        elif key == "hdrLook":
            row["aliases"] = ["hdrLighting"]
        elif key.startswith("blockEntityCache") and key != "blockEntityCache":
            row["aliases"] = ["blockEntityCache"]
        row["uwp_runtime_action"] = (f"Patched parser retains {field or key}; "
                                     "per-profile engine effects require trace or execution.")
        rows.append(row)
        coverage[f"official:{path.as_posix()}"].append(key)
    for ctl, (page, effect, pos) in ui_items.items():
        if not any(row["official_ui_page"] and
                   GRAPHICS_UI.get(row["key"], row["key"][0].upper() + row["key"][1:]) == ctl
                   for row in rows):
            row = setting_row("graphics.control", ctl, None, None, GRAPHICS_FIELDS.get(ctl),
                              [anchor("official", ui_path, ui, pos)], effect,
                              labels.get(ctl), f"Graphics / {page}", "No standalone stored key.")
            row["scope"] = "action"
            rows.append(row)
        coverage[f"official:{ui_path.as_posix()}"].append(f"H::{ctl}")
    # Server mappings are a separate, line-oriented file documented next to the parser.
    servers_pos = bluemap.index("pub const SERVERS_FILE")
    rows.append(setting_row(
        "graphics", "blueMap", "horizon-bluemap.txt", "empty list",
        "one server address and map URL per line",
        [anchor("official", map_path, bluemap, servers_pos)],
        "Select a BlueMap source for a named multiplayer server's distant terrain.",
        persisted="User-authored mapping file.",
    ))
    coverage[f"official:{map_path.as_posix()}"] = ["SERVERS_FILE"]
    return rows, coverage
