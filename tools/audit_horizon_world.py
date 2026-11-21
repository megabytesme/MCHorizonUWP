"""Extract controls and persisted values configured during offline world creation."""

import re
from collections import defaultdict
from pathlib import Path

from audit_horizon_vanilla import array, literal, ref, row, section, source, strings, where

CLIENT = "crates/horizon-client/src/"
TITLE = CLIENT + "title.rs"
CREATE = CLIENT + "title/create_world.rs"
PRESETS = CLIENT + "title/preset_editors.rs"
FLAT = CLIENT + "title/flat.rs"
BUFFET = CLIENT + "title/buffet.rs"
WORLD = CLIENT + "save.rs"
LOADING = CLIENT + "game/world/world_loading.rs"
RULES = "crates/horizon-client-core/src/gamerules.rs"
RULES_UI = CLIENT + "game_rules_screen.rs"
DATAPACK = "crates/horizon-client-render/src/datapack.rs"

LEVEL = "level.dat"
GEN = "data/minecraft/world_gen_settings.dat"
RULE_FILE = "data/minecraft/game_rules.dat"
CREATE_PAGE = "Create New World"
GAME_TAB = CREATE_PAGE + " > Game"
WORLD_TAB = CREATE_PAGE + " > World"
MORE_TAB = CREATE_PAGE + " > More"
RULES_PAGE = MORE_TAB + " > Game Rules"
FLAT_PAGE = WORLD_TAB + " > Customize > Superflat"
BOOL = {"type": "boolean"}

RULE_RE = re.compile(r'\b(?P<kind>b|i)\(\s*"(?P<key>\w+)",\s*"(?P<category>\w+)",\s*(?P<values>[^)]+)\)')
WORLD_BUTTONS = {
    20: "GameType", 21: "create", 22: "cancel", 23: "difficulty_settings.difficulty",
    24: "allowCommands", 25: "dimensions", 26: "customize",
    27: "generate_structures", 28: "bonus_chest", 29: "gameRules",
    30: "enabled_features", 31: "DataPacks",
}
RULE_INT_LIMITS = {"i32::MAX": 2147483647, "i32::MIN": -2147483648}


def rule_defs(root):
    src = source(root, RULES)
    rules, offset = array(src, "RULES")
    standard_rules = [(m, offset) for m in RULE_RE.finditer(rules)]
    if len(standard_rules) != len(re.findall(r'\b[bi]\(', rules)):
        raise ValueError("Unparsed vanilla game-rule declaration")
    extra = [(m, 0) for m in RULE_RE.finditer(src) if "VanillaRule = " in src[m.start() - 15:m.start()]]
    if {m["key"] for m, _ in extra} != {"max_minecart_speed"}:
        raise ValueError("Unparsed feature-dependent game-rule declaration")
    definitions = {}
    for m, base in standard_rules + extra:
        name = "minecraft:" + m["key"]
        tokens = [literal(part) for part in m["values"].strip().rstrip(",").split(",")]
        default = tokens[0]
        values = BOOL if m["kind"] == "b" else {
            "type": "integer", "min": RULE_INT_LIMITS.get(tokens[1], tokens[1]),
            "max": RULE_INT_LIMITS.get(tokens[2], tokens[2]),
        }
        at = f"{RULES}:{where(src, base + m.start())}"
        if name in definitions:
            raise ValueError(f"Duplicate game-rule declaration: {name}")
        definitions[name] = (values, default, m["category"], at)
    return definitions


def add_coverage(coverage, refs, key):
    for reference in refs:
        coverage[reference.rsplit(":", 1)[0]].append(key)


def existing_refs(official, uwp, evidence):
    matches = []
    for side, root in (("official", official), ("uwp", uwp)):
        for path, pattern in evidence:
            matches.append(ref(side, path, source(root, path), pattern))
    return matches


def creation_row(official, uwp, coverage, key, storage, values, default,
                 label, page, effect, evidence, conditions=(), action=False):
    refs = existing_refs(official, uwp, evidence)
    record = row(key, storage, values, default, refs, label, [page],
                 scope="world action" if action else "world setting")
    record["official_ui_label_key"] = label
    record["official_windows_behavior"] = effect
    record["expected_visible_effect"] = effect
    record["uwp_ui"] = "Present in shared source; packaged input and selectability untested."
    record["uwp_persistence"] = "Transient screen state." if storage.startswith("screen ") else "World writer present in source."
    record["uwp_runtime_action"] = effect
    record["test_method"] = "Use the control when creating an isolated world, inspect written data where applicable, and reload or observe the result."
    record["enabling_conditions"] = list(conditions)
    add_coverage(coverage, refs, key)
    return record


def creation_rows(official, uwp, coverage):
    src = source(official, CREATE)
    button_body, _ = section(src, "pub(super) fn buttons(", "pub(super) fn button_narration(")
    button_ids = {int(s) for s in re.findall(r'\b(\d+),\s*\)', button_body) if 20 <= int(s) <= 31}
    switch_body, _ = section(src, "pub(super) fn cycle(", "fn fixed_biome(")
    button_ids |= {int(s) for s in re.findall(r'(?m)^\s*(\d+) =>', switch_body)}
    button_ids |= {29 + i for i, _ in enumerate(re.finditer(r'"selectWorld\.(?:gameRules|experiments|dataPacks)"', button_body))}
    if button_ids != WORLD_BUTTONS.keys():
        raise ValueError(f"Unmapped create-world menu controls: {sorted(button_ids ^ WORLD_BUTTONS.keys())}")
    difficulty = strings(array(src, "DIFFICULTIES")[0])
    preset_src = source(official, WORLD)
    preset_body, _ = section(preset_src, "pub const fn id(self)", "pub fn of_id(")
    presets = strings(preset_body)
    name = re.search(r'name: lang::get_or\("selectWorld.newWorld", "([^"]+)"\)', source(official, TITLE))[1]

    specs = [
        ("LevelName", LEVEL, {"type": "text"}, name, "selectWorld.enterName", GAME_TAB,
         "Sets the world title. The game resolves a unique legal save folder.", TITLE, r"pub\(crate\) fn new_world", ()),
        ("GameType", LEVEL, {"type": "enum", "choices": ["survival", "hardcore", "creative"]}, "survival",
         "selectWorld.gameMode", GAME_TAB, "Sets the starting game mode. Hardcore uses Survival with the hardcore flag.",
         CREATE, r"let modes = ", ()),
        ("difficulty_settings.hardcore", LEVEL, BOOL, False, "selectWorld.gameMode.hardcore", GAME_TAB,
         "Hardcore locks difficulty to Hard, suppresses commands and the bonus chest, and starts a one-life world.",
         CREATE, r"pub fn difficulty", ("Select Hardcore on the Game tab.",)),
        ("difficulty_settings.difficulty", LEVEL, {"type": "enum", "choices": difficulty}, difficulty[2], "options.difficulty", GAME_TAB,
         "Chooses the initial world difficulty. Hardcore forces Hard.", CREATE, r'pub const DIFFICULTIES', ("Hardcore off",)),
        ("allowCommands", LEVEL, BOOL, "true in Creative; false otherwise", "selectWorld.allowCommands", GAME_TAB,
         "Controls command privileges in the new single-player world. Creative defaults on, Survival off and Hardcore forced off.",
         CREATE, r"pub fn commands", ("Hardcore off",)),
        ("seed", GEN, {"type": "signed 64-bit integer or text", "min": -9223372036854775808, "max": 9223372036854775807},
         "random when the field is blank", "selectWorld.enterSeed", WORLD_TAB,
         "A numeric seed goes to the generator directly. Text is converted via Java's UTF-16 string hash.",
         TITLE, r"pub fn parse_seed", ()),
        ("dimensions", GEN, {"type": "registry ID", "built_in": presets, "dynamic": "enabled minecraft:normal world-preset tag"},
         "minecraft:normal", "selectWorld.mapType", WORLD_TAB,
         "Selects generator and dimension stems. Alt opens the extended list including Debug Mode. Data packs can extend the normal list.",
         PRESETS, r"fn refresh_presets", ()),
        ("generate_structures", GEN, BOOL, True, "selectWorld.mapFeatures", WORLD_TAB,
         "Controls structure generation in the newly created world.", CREATE, r"27 => self\.structures", ()),
        ("bonus_chest", GEN, BOOL, False, "selectWorld.bonusItems", WORLD_TAB,
         "Requests a bonus chest near the initial spawn. Hardcore forces it off.", CREATE, r"pub fn bonus_chest", ("Hardcore off",)),
        ("enabled_features", LEVEL, {"type": "ordered per-pack toggle collection", "choices": "feature pack metadata"},
         ["minecraft:vanilla"], "selectWorld.experiments", MORE_TAB,
         "Enables experimental flags contributed by selected feature packs. The named switches depend on prepared or additional pack resources.",
         DATAPACK, r"pub fn feature_packs", ()),
        ("DataPacks", LEVEL, {"type": "ordered enabled/disabled pack selection", "choices": "available pack repository"},
         ["vanilla"], "selectWorld.dataPacks", MORE_TAB,
         "Selects data packs, persists their enabled and disabled lists, and loads the selected pack data during world generation.",
         LOADING, r"level\.data_packs = Some", ()),
        ("dimensions.minecraft:overworld.generator.biome_source.biome", GEN, {"type": "biome registry ID"},
         "minecraft:plains", "createWorld.customize.buffet.title", WORLD_TAB + " > Customize",
         "Uses the selected biome across the Overworld when the Single Biome preset is active.",
         CREATE, r"pub fn single_biome", ("Single Biome Surface preset",)),
        ("dimensions.*", GEN, {"type": "valid NBT dimension stems"}, "selected world preset", "selectWorld.customizeType", WORLD_TAB,
         "A registered preset editor opens a working copy. Applying valid dimensions writes them to the world generator settings.",
         PRESETS, r"pub fn set_preset_editor_dimensions", ("Enabled data pack or mod with a preset, registered compatible editor",)),
    ]
    rows = [creation_row(official, uwp, coverage, key, storage, values, default, label, page, effect,
                         [(path, pattern), (LOADING, r"if created && let Some\(o\) = create")], conditions)
            for key, storage, values, default, label, page, effect, path, pattern, conditions in specs]

    actions = [
        ("create", "selectWorld.create", CREATE_PAGE, "Commits the configured world and opens it.", TITLE, r"fn create_or_upload"),
        ("cancel", "gui.cancel", CREATE_PAGE, "Exits the creation screen without committing a world.", CREATE, r"\.does\(crate::widgets::Action::Cancel\)"),
        ("gameRules", "selectWorld.gameRules", MORE_TAB, "Opens the per-world rule editor.", TITLE, r"Some\(29\)"),
        ("customize", "selectWorld.customizeType", WORLD_TAB, "Opens the selected preset's editor when available.", CREATE, r'tr\("selectWorld.customizeType"\)'),
        ("flat.remove_layer", "createWorld.customize.flat.removeLayer", FLAT_PAGE, "Removes the selected layer from the draft superflat stack.", FLAT, r'"createWorld.customize.flat.removeLayer"'),
        ("flat.select_preset", "createWorld.customize.presets.select", FLAT_PAGE + " > Presets", "Applies the selected superflat preset or the edited share string.", FLAT, r"fn apply_share\("),
    ]
    rows.extend(creation_row(official, uwp, coverage, key, "screen action", {"type": "action"}, None,
                             label, page, effect, [(path, pattern)], action=True)
                for key, label, page, effect, path, pattern in actions)

    for field, values, default, effect in (
        ("biome", {"type": "biome registry ID"}, "minecraft:plains", "Sets the flat generator's biome through the selected preset or share string."),
        ("features", BOOL, False, "Preserves the chosen preset's decoration setting."),
        ("lakes", BOOL, False, "Preserves the chosen preset's lake-generation setting."),
        ("layers", {"type": "block ID and height per layer", "max_total_height": 4064},
         ["1 minecraft:bedrock", "2 minecraft:dirt", "1 minecraft:grass_block"],
         "Defines the bottom-to-top stack of blocks and heights."),
        ("structure_overrides", {"type": "list of structure-set registry IDs"},
         ["minecraft:strongholds", "minecraft:villages"], "Preserves the chosen preset's allowed structure sets."),
    ):
        key = f"dimensions.minecraft:overworld.generator.settings.{field}"
        item = creation_row(official, uwp, coverage, key, GEN, values, default,
                            "createWorld.customize.presets.share" if field in {"layers", "biome"} else None,
                            FLAT_PAGE, effect, [(FLAT, fr"pub {field}:"), (PRESETS, r"pub fn write_world_gen")],
                            ("Superflat preset",))
        if field not in {"biome", "layers"}:
            item["uwp_ui"] = "Derived from the chosen flat preset; no separate control is declared."
        rows.append(item)

    for storage, key, effect, evidence in (
        ("screen input", "flat.share", "Parses the flat preset's layer list and biome while preserving compatible base-preset flags.",
         [(FLAT, r"pub\(super\) fn apply_share")]),
        ("screen input", "buffet.search", "Filters the list of available biomes without changing the chosen biome.",
         [(BUFFET, r"pub\(super\) fn search_box")]),
    ):
        rows.append(creation_row(official, uwp, coverage, key, storage, {"type": "text"}, "",
                                 "createWorld.customize.presets.share" if key == "flat.share" else "createWorld.customize.buffet.search",
                                 FLAT_PAGE if key == "flat.share" else WORLD_TAB + " > Customize",
                                 effect, evidence))
    return rows


def rule_consumers(root):
    """Index calls through the public, typed vanilla-rules slots."""
    client = root / CLIENT
    gameplay = root / "crates/horizon-gameplay/src"
    paths = [client / "game.rs", *(client / "game").rglob("*.rs"), *gameplay.rglob("*.rs")]
    pattern = re.compile(r"\b(?:bool_at|int_at)\(\s*(?:[\w]+::)*vanilla::([A-Z_]+)")
    consumers = defaultdict(list)
    for path in sorted(paths):
        relative = path.relative_to(root).as_posix()
        parts = path.relative_to(root).parts
        if "tests" in parts or path.name.endswith(("test.rs", "tests.rs")):
            continue
        src = source(root, relative)
        for m in pattern.finditer(src):
            consumers[m[1].lower()].append(f"{relative}:{where(src, m.start())}")
    return consumers


def rule_rows(official, uwp, coverage):
    defs, port_defs = rule_defs(official), rule_defs(uwp)
    official_calls, port_calls = rule_consumers(official), rule_consumers(uwp)
    refs = existing_refs(official, uwp, [(RULES_UI, r"fn title_key|fn name_key|fn rule_name|format!\(\x22gamerule\."),
                                        (RULES, r"pub fn to_nbt")])
    rows = []
    for key, (values, default, group, reference) in defs.items():
        name = key.removeprefix("minecraft:")
        sources = ["official:" + reference, *refs]
        if key in port_defs:
            sources.append("uwp:" + port_defs[key][3])
        item = row(key, RULE_FILE, values, default, sources, "gamerule.minecraft." + name,
                   [RULES_PAGE + " > " + group], scope="world setting")
        item["aliases"] = [name]
        item["uwp_ui"] = "Listed by the shared Game Rules screen."
        item["uwp_persistence"] = "The shared GameRules writer serializes the rule as a namespaced NBT property."
        item["official_windows_behavior"] = "The integrated server receives the chosen rule in its world rule set."
        item["uwp_runtime_action"] = "The shared world loader copies the chosen rule into the new world's rule set. Gameplay behavior awaits an independent exercise."
        item["expected_visible_effect"] = "Changes the world policy for " + name.replace("_", " ") + "."
        item["test_method"] = "Use distinct settings for two isolated worlds, exercise the affected world behavior, and verify the value after reloading."
        for side, calls in (("official", official_calls), ("uwp", port_calls)):
            if calls[name]:
                item["sources"].extend(side + ":" + match for match in calls[name][:2])
        if port_calls[name]:
            item["uwp_runtime_action"] = "The patched source contains a typed consumer for this rule. Exercise the affected gameplay and verify the saved value after reloading."
        if name == "max_minecart_speed":
            item["enabling_conditions"] = ["Minecraft minecart improvements experiment is enabled."]
        add_coverage(coverage, sources, key)
        rows.append(item)
    return rows


def inventory(official: Path, uwp: Path) -> tuple[list[dict], dict[str, list[str]]]:
    """Return world creation rows and the source files containing their declarations."""
    coverage = defaultdict(list)
    rows = creation_rows(official, uwp, coverage)
    rows.extend(rule_rows(official, uwp, coverage))
    rows.extend(live_world_rows(official, uwp, coverage))
    rows.extend(edit_world_rows(official, uwp, coverage))
    return rows, {path: sorted(set(entries)) for path, entries in sorted(coverage.items())}



LAN_UI = CLIENT + "game/network/lan_screen.rs"
EDIT_BUTTONS = CLIENT + "title/preset_buttons.rs"


def live_world_rows(official, uwp, coverage):
    src = source(official, LAN_UI)
    declarations, _ = section(src, "enum Button {", "fn on_off(")
    declared = set(re.findall(r"^ {4}(\w+),", declarations, re.M))
    page = "Pause > World Options"
    modes = {"type": "enum", "choices": strings(array(src, "MODES")[0])}
    difficulty = {"type": "enum", "choices": strings(array(src, "DIFFICULTIES")[0])}
    controls = [
        ("DefaultMode", "GameType", LEVEL, modes, "options.worldOptions.game_mode",
         "Sets the default mode for players joining the world.", ("Commands enabled", "Hardcore off")),
        ("PersonalMode", "playerGameType", "playerdata", modes, "options.worldOptions.personal_game_mode",
         "Changes the local player's active game mode.", ("Commands enabled", "Hardcore off")),
        ("Cheats", "allowCommands", LEVEL, BOOL, "selectWorld.allowCommands",
         "Grants or revokes the local owner's command access.", ("Host",)),
        ("Difficulty", "difficulty_settings.difficulty", LEVEL, difficulty, "options.difficulty",
         "Changes the world's active difficulty, or sends a change request to an authorized remote server.",
         ("Difficulty unlocked", "Hardcore off", "Gamemaster permission on remote servers")),
        ("Lock", "difficulty_settings.locked", LEVEL, BOOL, "narrator.button.difficulty_lock",
         "Confirms and persists the difficulty lock.", ("Difficulty unlocked", "Hardcore off")),
        ("GameRules", "gameRules", "screen action", {"type": "action"}, "editGamerule.inGame.button",
         "Opens the in-world rules editor.", ("Permission to read or edit the current server's rules",)),
        ("Restrictions", "restrictions", "screen action", {"type": "action"}, "restrictions_screen.button",
         "Opens the restrictions screen for the active world.", ()),
        ("Lan", "lan", "runtime LAN session", BOOL, "menu.multiplayerOptions.lan",
         "Asks the integrated server to publish or close the world on the local network.", ("Host",)),
        ("CommandAccess", "guest_commands", "runtime LAN session", BOOL, "options.worldOptions.guest.command_access",
         "Grants eligible LAN guests command access.", ("LAN open", "Host commands enabled")),
        ("Force", "force_game_mode", "runtime LAN session", BOOL, "options.worldOptions.guest.force_game_mode",
         "Forces LAN guests into the default game mode.", ("LAN open", "Hardcore off", "Guest commands off")),
        ("Apply", "apply", "screen action", {"type": "action"}, "menu.multiplayerOptions.applyChanges",
         "Submits valid changed values to the integrated or remote server.", ("Settings changed", "Valid LAN port when sharing")),
        ("Cancel", "cancel", "screen action", {"type": "action"}, "gui.cancel",
         "Closes the screen and abandons pending changes.", ()),
    ]
    if declared != {item[0] for item in controls}:
        raise ValueError(f"Unmapped live World Options controls: {declared ^ {item[0] for item in controls}}")
    rows = []
    for control, key, storage, values, label, effect, conditions in controls:
        record = creation_row(official, uwp, coverage, key, storage, values,
                              "current world's or session's value", label, page, effect,
                              [(LAN_UI, fr"Button::{control} =>"), (LAN_UI, r"fn apply_world_options")],
                              conditions, action=storage == "screen action")
        record["id"] = f"live world:{key}"
        record["uwp_persistence"] = "Applied to the live world state." if storage == LEVEL else "Session action or value."
        record["test_method"] = "Apply changes in an isolated world and verify the server outcome. Where persistent, restart the same world and check the value."
        rows.append(record)
    item = creation_row(official, uwp, coverage, "port", "runtime LAN session",
                        {"type": "integer", "min": 1024, "max": 65535},
                        "suggested free port if blank", "lanServer.port", page,
                        "Chooses the TCP listener port used when publishing the game on the local network.",
                        [(LAN_UI, r"fn parsed_port"), (LAN_UI, r"self\.lan_open\(LanSettings")],
                        ("LAN enabled", "Local host"))
    item["test_method"] = "On two authorized devices, publish an isolated world on the selected port, join, then close it and confirm the test listener closes."
    item["uwp_persistence"] = "Session value."
    rows.append(item)
    return rows


def edit_world_rows(official, uwp, coverage):
    page = "Singleplayer > Edit World"
    known = {
        "resetIcon": "Resets the world's cached icon so a future visit can regenerate it.",
        "openFolder": "Requests opening the selected world's folder.",
        "backup": "Creates a backup archive of the selected world.",
        "backupFolder": "Requests opening the world's backup folder.",
        "optimize": "Starts the saved chunk data upgrade flow after confirmation.",
        "save": "Saves the edited world name.",
    }
    src = source(official, EDIT_BUTTONS)
    labels = set(re.findall(r'"selectWorld\.edit\.(\w+)"', src))
    if labels != known.keys():
        raise ValueError(f"Unmapped Edit World labels: {labels ^ known.keys()}")
    rows = []
    for name, effect in known.items():
        item = creation_row(official, uwp, coverage, name, "screen action", {"type": "action"}, None,
                            f"selectWorld.edit.{name}", page, effect,
                            [(EDIT_BUTTONS, fr'"selectWorld\.edit\.{name}"')], action=True)
        item["test_method"] = "Exercise the action against a disposable copied world and inspect the resulting UI or artifact."
        rows.append(item)
    item = creation_row(official, uwp, coverage, "erase_cache", "screen action", BOOL, False,
                        "selectWorld.backupEraseCache", page + " > Optimize", "Includes cached world data in the optimization cleanup.",
                        [(TITLE, r"erase_cache: false"), (CLIENT + "title/draw.rs", r"selectWorld\.backupEraseCache")])
    item["test_method"] = "Use a disposable copied world, run both optimization paths, and inspect only that copy."
    rows.append(item)
    return rows
