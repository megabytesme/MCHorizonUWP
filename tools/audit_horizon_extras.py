"""Extract Horizon extras, PvP and legacy configuration."""

import json
import re
from pathlib import Path

from audit_horizon_core import (
    EXTRA_LABELS, EXTRA_UI, RENDER, anchor, declaration, default_fields, literal_arms,
    locations, read, rule, section, setting_row,
)


ENGLISH = Path("crates/horizon-client/assets/horizon/lang/en_us.json")
EXTRA_CONSTANTS = {
    "SKIN_LAYERS_KEY": "threeDSkinLayers",
    "EMOTE_KEY": "emoteKey",
    "horizon_core::no_ai::KEY": "noAi",
}
EXTRA_PAGES = {
    "agentControl": "Horizon Settings / AI Agent",
    "reproKeys": "Horizon Settings / AI Agent",
    "noAi": "Horizon Settings",
    "splitscreen": "Horizon Settings / Splitscreen",
    "splitscreenTwo": "Horizon Settings / Splitscreen",
    "splitscreenThree": "Horizon Settings / Splitscreen",
    "splitscreenNames": "Horizon Settings / Splitscreen",
    "emoteKey": "Controls / Key Binds",
    "sortKey": "Horizon Extras; Controls / Key Binds",
    "musicOff": "Music & Sounds / Music",
    "threeDSkinLayers": "Skin Customization",
    "lodZoomDetail": "Horizon Extras / Zoom",
}
SPECIAL_EXTRA = {
    "emoteKey": ("key.* binding", "key_key.emote"),
    "zoomKey": ("key.* binding", None),
    "sortKey": ("key.* binding", None),
    "minimapShape": ("square / round", None),
    "minimapSize": ("64 / 96 / 128 GUI pixels", None),
    "minimapZoom": ("0.5 / 1.0 / 2.0 / 4.0 blocks per pixel", None),
    "movedToMods": ("comma-separated mod IDs", None),
    "musicOff": ("comma-separated disabled music identifiers", None),
    "threeDSkinLayers": ("true / false", "options.txt:threeDSkinLayers"),
    "splitscreenTwo": ("0 / 1 / 2", None),
    "splitscreenThree": ("0 / 1 / 2", None),
}
PVP_TYPES = {
    "c": "Crosshair", "o": "Outline", "h": "Hit", "u": "Hud", "t": "Tweaks",
    "self.chat": "ChatOptions", "self.combat": "Combat", "self": "Pvp",
}
PVP_OPTIONS = {
    "hudPlace": ("HUD editor placement and scale", "widget, corner or normalized coordinates, scale"),
    "hudEditorKey": ("Edit HUD", "key.* binding"),
    "combatFeel": ("Combat Feel", "modern / legacy"),
    "outlineFill": ("Fill", "off or named colour"),
    "crosshairHover": ("Colour on entities", "off or named colour"),
    "glintColour": ("Glint Colour", "off or named colour"),
    "viewWeather": ("View Weather", "off / clear / rain / thunder"),
}


def annotated_settings(official, uwp):
    config = RENDER / "extras.rs"
    graphics_ui = RENDER / "graphics_ui.rs"
    source = read(official, config)
    patched = read(uwp, config)
    english = json.loads(read(official, ENGLISH))
    default = default_fields(source, "impl Default for Extras", "pub const MOVED_READ_MAX")
    caption_section, _ = section(source, "pub fn caption_key(", "pub fn description_key(")
    captions = dict(re.findall(r'Item::(\w+) => "([^"]+)"', caption_section))
    desc_section, _ = section(source, "pub fn description_key(", "pub fn caption_text(")
    descriptions = dict(re.findall(r'Item::(\w+) => "([^"]+)"', desc_section))
    graphic_pages = locations(read(official, graphics_ui), "Item")
    moved_section, _ = section(source, "pub const MOVED_KEYS", "#[derive(")
    moved_keys = {}
    for moved in re.finditer(r'\(\s*"([^"]+)",\s*&\[([^]]+)\]', moved_section):
        moved_keys.update({key: moved[1] for key in re.findall(r'"([^"]+)"', moved[2])})
    patched_keys = {name: pos for name, _, pos in literal_arms(
        patched, "match key.trim() {", "pub fn serialize(", EXTRA_CONSTANTS)}
    keys = []
    rows = []
    for key, arm, pos in literal_arms(
        source, "match key.trim() {", "pub fn serialize(", EXTRA_CONSTANTS,
    ):
        field = re.search(r"e\.(\w+)\s*=", arm)[1]
        initial = default.get(field)
        effect, _ = declaration(source, field)
        control = EXTRA_UI.get(key, key[0].upper() + key[1:])
        label = EXTRA_LABELS.get(key) or english.get(captions.get(control))
        label = {
            "emoteKey": "key.emote",
            "musicOff": "Track title from the current music list",
            "threeDSkinLayers": english["horizon.options.three_d_skin_layers"],
        }.get(key, label)
        desc = english.get(descriptions.get(control))
        page = EXTRA_PAGES.get(key, "Horizon Extras" if label else None)
        sources = [anchor("official", config, source, pos)]
        if control in graphic_pages:
            gp, _, gp_pos = graphic_pages[control]
            page = f"Graphics / {gp}"
            graphic_source = read(official, graphics_ui)
            sources.append(anchor("official", graphics_ui, graphic_source, gp_pos))
        if key in patched_keys:
            sources.append(anchor("uwp", config, patched, patched_keys[key]))
        values, alias = SPECIAL_EXTRA.get(key, (rule(arm, initial), None))
        if "Corner::parse(" in arm:
            values = "top-left / top-right / bottom-left / bottom-right"
        row = setting_row("extras", key, "horizon-extras.txt", initial, values, sources,
                          effect or desc, label, page)
        if not page:
            row["scope"] = "hidden"
        if alias:
            row["aliases"] = [alias]
        if key in moved_keys:
            mod = moved_keys[key]
            row.update(scope="legacy migration", official_ui_label=None, official_ui_page=None,
                       official_windows_behavior=f"Legacy setting read for migration into {mod}.",
                       expected_visible_effect=f"Depends on installed and enabled {mod} settings.",
                       uwp_ui="Legacy key; official mod provides its own UI.",
                       uwp_runtime_action="Shared legacy parser and optional official mod integration.",
                       enabling_conditions=f"Install the corresponding version of {mod} and test migration.")
        elif key == "reproKeys" or key == "agentControl":
            row["scope"] = "developer"
        elif key not in patched_keys:
            row["uwp_persistence"] = "No matching parse declaration found."
        else:
            row["uwp_runtime_action"] = f"Patched parser retains {field}; runtime effect requires verification."
        rows.append(row)
        keys.append(key)
    ui_covered = [
        f"Item::{name}" for name in captions
        if name in graphic_pages or any(
            EXTRA_UI.get(key, key[0].upper() + key[1:]) == name for key in keys)
    ]
    for name in captions:
        if f"Item::{name}" not in ui_covered:
            raise ValueError(f"Unmapped Extras control {name}")
    return rows, {
        f"official:{config.as_posix()}": keys + ui_covered,
        f"official:{graphics_ui.as_posix()}": [f"Extra::{item}" for item in graphic_pages],
    }


def pvp_settings(official, uwp):
    config = RENDER / "pvp.rs"
    source = read(official, config)
    patched = read(uwp, config)
    default = {
        kind: default_fields(source, f"impl Default for {kind}", "\n}")
        for kind in PVP_TYPES.values() if kind not in {"ChatOptions", "Combat"}
    }
    control_text, offset = section(source, "\nopts! {", "\n}")
    controls = {
        match[1]: (match[3], match[4], offset + match.start()) for match in re.finditer(
            r'(?m)^    (\w+), "([^"]+)", "([^"]+)",\s*"([^"]+)";', control_text)
    }
    patched_keys = {name: pos for name, _, pos in literal_arms(
        patched, "pub fn parse_line(", "pub fn serialize(", indent=12)}
    keys = []
    ui_keys = []
    rows = []
    for key, arm, pos in literal_arms(source, "pub fn parse_line(", "pub fn serialize(", indent=12):
        control = key[0].upper() + key[1:]
        if key == "lowFire":
            control = "FireLower"
        ctl = controls.get(control)
        field = re.search(r"\b(c|o|h|u|t|self\.chat|self\.combat|self)\.(\w+)\s*=", arm)
        initial = None
        if field:
            family = PVP_TYPES[field[1]]
            initial = default.get(family, {}).get(field[2])
            if family in {"ChatOptions", "Combat"}:
                initial = "false"
        if key == "combatFeel":
            initial = "modern"
        elif key == "hudPlace":
            initial = "empty layout; use widget defaults"
        sources = [anchor("official", config, source, pos)]
        if key in patched_keys:
            sources.append(anchor("uwp", config, patched, patched_keys[key]))
        if ctl:
            sources.append(anchor("official", config, source, ctl[2]))
            ui_keys.append(f"Opt::{control}")
        label = ctl[0] if ctl else None
        effect = ctl[1] if ctl else None
        values = rule(arm, initial)
        if cap := re.search(r"num\(value, (\w+),", arm):
            bounds = re.search(rf"const {cap[1]}: [^=]+=\s*\(([^,]+),\s*([^)]+)\)", source)
            if bounds:
                values = f"{bounds[1]}–{bounds[2]}"
        elif "Corner::parse(" in arm:
            values = "top-left / top-right / bottom-left / bottom-right"
        elif parser := re.search(r"(Colour|Shape|ViewTime|ViewWeather)::parse", arm):
            body, _ = section(source, f"impl {parser[1]} {{", "\n}")
            names = re.findall(rf'{parser[1]}::\w+ => "([^"]+)"', section(body, "pub fn name(", "\n    }")[0])
            values = " / ".join((["off"] if "=> None" in arm else []) + names)
        if key in PVP_OPTIONS:
            fallback, values = PVP_OPTIONS[key]
            label = label or fallback
        page = "PvP & HUD" if label else None
        if key == "hudEditorKey":
            page = "Controls / Key Binds"
        if key == "hudPlace":
            page = "PvP & HUD / HUD editor"
            effect = "Moves and scales each enabled HUD widget while preserving its content."
        row = setting_row("extras.pvp", key, "horizon-extras.txt", initial, values, sources,
                          effect, label, page)
        row["uwp_runtime_action"] = "Shared PvP route; observe effect in the signed game."
        rows.append(row)
        keys.append(key)
    for control, (label, effect, pos) in controls.items():
        if f"Opt::{control}" in ui_keys:
            continue
        row = setting_row("extras.pvp.control", control, None, None, None,
                          [anchor("official", config, source, pos)], effect,
                          label, "PvP & HUD", "No standalone stored key established.")
        row["scope"] = "action"
        rows.append(row)
        ui_keys.append(f"Opt::{control}")
    return rows, {f"official:{config.as_posix()}": keys + ui_keys}


def inventory(official, uwp):
    first, first_coverage = annotated_settings(official, uwp)
    second, second_coverage = pvp_settings(official, uwp)
    return first + second, {**first_coverage, **second_coverage}
