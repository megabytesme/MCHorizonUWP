"""Extract shipped shader controls and graphics-preset values."""

import json
import re
import tomllib
from pathlib import Path

from audit_horizon_core import RENDER, anchor, read, section, setting_row
from audit_horizon_evidence import DX11, apply_cells


SHADER_LABELS = {
    "Pack": "Pack filename or manifest display name",
    "Folder": "Open Pack Folder",
    "Option": "Selected pack's option label",
    "Link": "Selected pack's page label",
    "Profile": "Profile",
    "Reset": "Reset Options",
    "ShadowCache": "Shadow Cache",
}
HDR_STEPS = {
    "Black": ("Black Level", "hdrBlackLevel"),
    "Peak": ("Peak Brightness", "hdrPeak"),
    "PaperWhite": ("Paper White", "hdrPaperWhite"),
}


def dynamic_controls(official, port):
    rows = []
    coverage = {}
    shader_ui, hdr_ui = (RENDER / name for name in ("shader_ui.rs", "hdr_calibration.rs"))
    for relative, enum, labels, namespace, page in (
        (shader_ui, "pub(crate) enum Item {", SHADER_LABELS, "shader.control", "Graphics / Shaders"),
        (hdr_ui, "pub enum Step {", HDR_STEPS, "hdr.calibration", "Graphics / HDR / Calibration"),
    ):
        source, patched = read(official, relative), read(port, relative)
        body, offset = section(source, enum, "\n}")
        covered = []
        for found in re.finditer(r"(?m)^(?P<doc>(?:    ///[^\n]*\n)*)    (?P<name>\w+)(?:\([^)]*\))?,", body):
            name = found["name"]
            declaration = (found["doc"] or "").replace("    ///", "").replace("\n", " ").strip()
            label = labels[name]
            writes = None
            if isinstance(label, tuple):
                label, writes = label
            refs = [anchor("official", relative, source, offset + found.start())]
            if found.group() in patched:
                refs.append(anchor("uwp", relative, patched, patched.index(found.group())))
            row = setting_row(namespace, name, None, None, None, refs, declaration or None,
                              label, page, "No separate key." if writes is None else f"On Save writes {writes}.")
            row["scope"] = "action"
            if writes:
                row["aliases"] = [writes]
            if relative == shader_ui:
                apply_cells(row, DX11, "H", "The compatibility route can display shared pack UI; it cannot execute pack stages.")
            rows.append(row)
            covered.append(name)
        coverage[f"official:{relative.as_posix()}"] = covered
    return rows, coverage


def parse_preset_tables(official, settings, coverage):
    path = RENDER / "graphics_preset.rs"
    source = read(official, path)
    body, offset = section(source, "fn table(", "pub fn apply_preset(")
    cases = re.finditer(r"Preset::(\w+) => Table \{([^}]+)\}", body)
    result = {}
    for case in cases:
        values = dict(re.findall(r"(?m)^\s+(\w+): ([^,\n]+),$", case[2]))
        result[case[1]] = {"values": values, "source": anchor("official", path, source, offset + case.start())}
    shared, apply_offset = section(source, "pub fn apply_preset(", "\n}")
    assignments = {match[1]: match[2] for match in re.finditer(
        r"(?m)^    ([sg]\.\w+) = ([^;\n]+);", shared,
    )}
    preset = next(row for row in settings if row["id"] == "graphics.preset")
    preset["preset_tables"] = result
    preset["preset_assignments"] = assignments
    preset["sources"].append(anchor("official", path, source, apply_offset))
    preset["uwp_runtime_action"] = "Writes individual graphics and vanilla values. Each resulting effect still uses its own renderer gate."
    coverage[f"official:{path.as_posix()}"] = list(result) + list(assignments)


def bundled_pack_settings(official, coverage):
    rows = []
    package_dir = "crates/horizon-shader-api/packs"
    manifests = sorted((official / package_dir).glob("*/horizon-shaders.toml"))
    for manifest in manifests:
        package = manifest.parent.name
        document = read(official, manifest.relative_to(official))
        data = tomllib.loads(document)
        offsets = [m.end() for m in re.finditer(r"(?m)^\[\[options\]\]", document)]
        entries = data.get("options", [])
        if len(offsets) != len(entries):
            raise ValueError(f"Cannot map shader option source lines in {package}")
        relative = manifest.relative_to(official)
        for entry, position in zip(entries, offsets):
            key = entry["id"]
            kinds = {name: value for name, value in entry.items()
                     if name not in {"id", "default", "name", "label", "description", "tooltip"}}
            row = setting_row(
                f"shader.pack.{package}", key, f"shaderpacks/<{package} pack filename>.txt",
                entry.get("default"), kinds, [anchor("official", relative, document, position)],
                entry.get("description", entry.get("tooltip")), entry.get("name", entry.get("label")),
                f"Graphics / Shaders / {package}", "Separate per-pack options persisted by shared loader.",
            )
            row["enabling_conditions"] = "Load the exact native pack on a backend that runs its declared stages."
            row["uwp_runtime_action"] = "Native pack uniform option; match the program and stage in the shader matrix."
            apply_cells(row, DX11, "H", "Current D3D11 compatibility path does not run pack stages.")
            rows.append(row)
        coverage[f"official:{relative.as_posix()}"] = [entry["id"] for entry in entries]
    return rows

COSMETICS = "crates/horizon-client/src/game/interface/cosmetics/settings.rs"
PREFERENCES = "crates/horizon-cosmetics/src/prefs.rs"
MOD_LIST = "crates/horizon-mod-package/src/mod_list.rs"


def registered_settings(official, uwp):
    paths = [Path(path) for path in (COSMETICS, PREFERENCES, MOD_LIST)]
    cosmetics_path, prefs_path, mods_path = paths
    cosmetics, prefs, mods = (read(official, path) for path in paths)
    patched_cosmetics, patched_mods = (read(uwp, path) for path in (cosmetics_path, mods_path))
    english = json.loads(read(official, Path("crates/horizon-client/assets/horizon/lang/en_us.json")))
    constants = dict(re.findall(r'const (\w+): &str = "([^"]+)";', prefs + "\n" + cosmetics))
    fields, start = section(cosmetics, "    rows.extend([", "]);")
    ui = list(re.finditer(r"(?:toggle|cycle)\(ROWS\[(\d+)\], (?:shown\()?(?:s\.|seat\.)?(\w+)", fields))
    labels = re.findall(r'"([^"]+)"', section(cosmetics, "const ROWS:", "const TOOLTIPS:")[0])
    default, _ = section(cosmetics, "impl Default for Settings", "const KEYS:")
    defaults = dict(re.findall(r"(?m)^\s+(\w+): (\w+),$", default))
    parsed, parse_start = section(cosmetics, "match key {", "_ => {}")
    cases = list(re.finditer(r"(?m)^ {12}(?:prefs::)?(\w+) => self\.(\w+) =", parsed))
    if len(cases) != len(ui) or len(ui) != len(labels):
        raise ValueError("Unmapped cosmetics settings or controls")
    ui_fields = {hit[2]: hit for hit in ui}
    rows = []
    for case in cases:
        constant, field = case.groups()
        key = constants[constant]
        control = ui_fields[field]
        name = labels[int(control[1])]
        ui_point = start + control.start()
        refs = [
            anchor("official", cosmetics_path, cosmetics, parse_start + case.start()),
            anchor("official", cosmetics_path, cosmetics, ui_point),
            anchor("uwp", cosmetics_path, patched_cosmetics, patched_cosmetics.index(case.group())),
        ]
        for path, source in ((prefs_path, prefs), (cosmetics_path, cosmetics)):
            if f'const {constant}: &str = "{key}"' in source:
                refs.append(anchor("official", path, source, source.index(f'const {constant}: &str')))
        effect = english.get(f"{name}.tooltip")
        values = "shown / name_tags / hidden; legacy bool is accepted" if field == "others" else "true / false"
        row = setting_row("extras.cosmetics", key, "horizon-extras.txt", defaults[field], values,
                          refs, effect, english.get(name, name), "Horizon Extras / Cosmetics")
        row.update(uwp_runtime_action="Reads the chosen preference in the shared seat and cosmetics service route.",
                   enabling_conditions="Cosmetics service offered, with matching account or local-world conditions.")
        rows.append(row)
    coverage = {
        f"official:{cosmetics_path.as_posix()}": [row["key"] for row in rows] + labels,
        f"official:{prefs_path.as_posix()}": [row["key"] for row in rows if row["key"] != constants["SOLO"]],
    }
    static = [
        ("mod.<id>", 'strip_prefix("mod.")', "Off for content/Wasm; registered native mod On",
         "true / false", "Installed mod toggle", "Options / Mods",
         "Selects an installed mod for game use; package admission also applies."),
        ("key.<id>.<name>", 'strip_prefix("key.")', "Mod's registered binding",
         "Registered key name with optional Shift/Ctrl/Alt modifiers", "Mod's registered key label",
         "Options / Controls", "Binds mod action to the selected input and modifiers."),
        ("wasm.memory_cap_mib", "k.trim() == WASM_MEMORY_CAP", "4096 MiB (manifest limit)",
         "1–4096 MiB", None, None, "Limits each interpreted Wasm group's declared memory for the instance."),
    ]
    for key, needle, initial, values, label, page, effect in static:
        refs = [anchor(side, mods_path, src, src.index(needle))
                for side, src in (("official", mods), ("uwp", patched_mods))]
        row = setting_row("mods", key, "horizon-mods.txt", initial, values, refs, effect, label, page)
        row.update(scope="settings" if page else "developer",
                   uwp_runtime_action="Shared mod configuration; execution needs a compatible mod and package policy.",
                   enabling_conditions="Valid mod ID, registration and engine/ABI match.")
        rows.append(row)
    coverage[f"official:{mods_path.as_posix()}"] = [item[0] for item in static]
    return rows, coverage
