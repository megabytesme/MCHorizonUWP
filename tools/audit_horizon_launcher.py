"""Extract the pinned desktop launcher's persisted settings, Settings cards and CLI."""

import re
from pathlib import Path

from audit_horizon_core import anchor, read, section, setting_row


BACKEND = Path("launcher/src/backend.rs")
LOOK = Path("launcher/src/backend/look_settings.rs")
EFFECTS = Path("launcher/src/effects.rs")
LANGUAGE = Path("launcher/src/i18n.rs")
GLASS = Path("launcher/src/glass_settings.rs")
STORAGE = Path("launcher/src/glass_storage.rs")
TELEMETRY = Path("launcher/src/telemetry.rs")
CONSENT = Path("crates/horizon-telemetry/src/consent.rs")
CLI = Path("launcher/src/cli.rs")
DEVELOPER = Path("launcher/src/cli/developer_builds.rs")
PRODUCT = Path(__file__).resolve().parents[1]
SHELL = Path("uwp/pregame/host/XamlShell.cpp")
BUILDER = Path("uwp/builder/README.md")

# key: (Settings page, displayed label, effective default, accepted values, CLI alias)
FIELDS = {
    "selected": (None, None, "Unset", "Installed version tag resolved by launcher", "selected-version"),
    "game_architecture": ("Game", "Game architecture", "auto", ["auto", "x64", "arm64"], "game-architecture"),
    "instance": (None, None, "Horizon home", "Instance id", None),
    "minecraft_dir": (None, None, "Detected/launcher default", "Path or default", "minecraft-dir"),
    "server_dir": (None, None, "Launcher server folder", "Path or default", "server-dir"),
    "install_new_versions": ("Updates", "Install new versions automatically", True, "Boolean", "install-new-versions"),
    "prune_old_versions": ("Updates", "Delete old versions automatically", True, "Boolean", "prune-old-versions"),
    "keep_versions": (None, None, 2, "Integer ≥ 1 or default", "keep-versions"),
    "skip_opening": ("General", "Show the Horizon logo at startup", False, "Boolean stored as inverse of the visible action", "opening-animation"),
    "desktop_entry": ("About", "Applications menu", True, "Boolean on Linux", None),
    "game_args": ("Game", "Arguments", "Empty string", "One line split into game arguments", "game-args"),
    "proxy": ("Network", "Proxy", "HTTPS_PROXY / HTTP_PROXY when configured", "HTTP/SOCKS5 URL or default", "proxy"),
    "appearance": (None, None, "Dark", "Old light selection migrates to Taiga", None),
    "ping_notice_seen": (None, None, False, "Boolean notice state", None),
    "ping_off": ("Data & privacy", '"I\'m playing" ping', False, "Boolean stored as the inverse of the visible switch", None),
    "effects": (None, None, "All flags on", "Effects object, three Boolean members", None),
    "welcomed": (None, None, False, "Boolean notice state", None),
    "owner_notice_seen": (None, None, False, "Boolean notice state", None),
    "partner_seen_secs": (None, None, 0, "Seconds clipped at hide threshold", None),
    "partner_hidden": (None, None, False, "Boolean", None),
    "while_playing": ("General", "While playing", "stay", "stay / minimize / hide (hide minimizes on Windows)", "while-playing"),
    "accent": ("Appearance", "Accent", "Horizon violet", "Named accent, RGB hex, or hue in degrees", "accent"),
    "glass": ("Appearance", "Glass", "6%", "0–16 percent", None),
    "blur": ("Appearance", "Blur", "24 px", "0–48 pixels", None),
    "theme": ("Appearance", "Theme", "Horizon", "Launcher theme id", None),
    "performance": ("Appearance", "Performance mode", False, "Boolean", None),
    "background": ("Appearance", "Home background", "Horizon landscape", "Owned image path; unset restores default", "background"),
    "language": ("General", "Language", "System language", "Launcher Language enum or system/default", "language"),
    "mods_auto_update": ("Game", "Update registry mods automatically", True, "Boolean", None),
    "native_mods_allowed_at": (None, None, "Unset / no native build approval", "Unix seconds, established by explicit approval", None),
    "cache_limit_gib": ("Storage", "Cache limit", 25, "10 / 25 / 50 / 100 GiB, or 0 to disable automatic cleanup", "cache-limit-gib"),
    "theme_accents": (None, None, "Empty map", "Theme id → persisted accent", None),
    "reduce_motion": ("Appearance", "Motion", "Follows system", "Unset / true / false", None),
}

EFFECT_LABELS = {"glow": "Glow", "gradients": "Gradients", "sheen": "Shine on hover"}

FIELD_DESCRIPTIONS = {
    "selected": "Selects an installed Horizon release for subsequent default launch and preparation.",
    "minecraft_dir": "Selects the Minecraft installation inspected by resource preparation.",
    "server_dir": "Selects the launcher-managed dedicated-server folder.",
    "install_new_versions": "Installs available Horizon game releases automatically when enabled.",
    "effects": "Persists the appearance switches used by the launcher each time it draws.",
}

# Each row describes the corresponding UWP feature at the scope the product exposes.
# Product handles with different semantics keep partial status.
PORT_FIELDS = {
    "selected": ("P", BUILDER, "signed compatibility catalog", "Builder selects a pinned package recipe. The UWP app launches its packaged build."),
    "game_architecture": ("P", BUILDER, "supports x86, x64", "Builder offers target architectures. UWP game process architecture is selected when packaged."),
    "minecraft_dir": ("P", BUILDER, "prepared resources", "Builder accepts prepared resources or prepares owned game assets. The target app receives the packaged resource framework."),
    "native_mods_allowed_at": ("P", BUILDER, "Native mods run", "Builder's trusted native source and signed package staging use a distinct workflow."),
    "install_new_versions": ("P", SHELL, "Check for updates at startup", "Client checks release availability and presents notices. The Builder has its own update preference."),
    "theme": ("P", SHELL, "App style", "XAML offers system material and style options with its own persisted key."),
    "reduce_motion": ("?", SHELL, "App style", "Comparable per-app optional motion override needs source or device proof."),
    "language": ("?", SHELL, "App style", "A standalone equivalent to the desktop launcher language chooser was not established. The installed game's Language is a separate control."),
    "while_playing": ("P", SHELL, "When Minecraft quits", "The packaged host handles the launcher-to-game handoff within its app view and configures exit behavior."),
    "ping_off": ("P", SHELL, "add_toggle(8,", "The host exposes a live player-count switch for each endpoint. Its consent store and defaults differ from the desktop playing-ping preference."),
    "desktop_entry": ("H", SHELL, "live-tile-options", "The official setting is Linux-only. The UWP package has Windows Start tile settings."),
}

CLI_CONTAINER = re.compile(r"(?m)^(?:pub\(super\) )?(?:enum|struct) (\w+)(?:\s*\{)")
VARIANT = re.compile(r"(?m)^(?:    )([A-Z]\w*)(?=\s*(?:\{|\(|,))")
ARGUMENT = re.compile(
    r"(?m)^(?P<indent> +)(?P<name>[a-z][a-z0-9_]*): (?P<type>[^,\n]+),?$"
)


def documented_fields(source, start, end):
    body, offset = section(source, start, end)
    pattern = re.compile(
        r"(?m)^(?P<docs>(?:    ///[^\n]*\n)*)(?:    #\[[^\n]*\]\n)*"
        r"    pub (?P<name>\w+): (?P<type>[^\n]+),$"
    )
    for item in pattern.finditer(body):
        docs = " ".join(line.strip()[4:] for line in item["docs"].splitlines())
        yield item["name"], docs, offset + item.start("name")


def tracked_port(row, evidence_key):
    default = ("U", SHELL, "if (show_settings && prompt_kind == 0)",
               "The UWP shell and Builder do not expose this official launcher setting in the audited surface.")
    status, path, needle, behavior = PORT_FIELDS.get(evidence_key, default)
    source = read(PRODUCT, path)
    row["sources"].append(anchor("product", path, source, source.index(needle)))
    row.update(uwp_uniform_status=status, owner="MCHorizonUWP", uwp_runtime_action=behavior,
               uwp_ui="UWP XAML shell / Builder surface; see behavior.",
               uwp_persistence="The UWP path does not read official launcher settings.json.",
               root_cause=behavior if status in {"U", "H", "P"} else None)


def settings(official, coverage):
    source = read(official, BACKEND)
    found = dict((name, (docs, line)) for name, docs, line in documented_fields(
        source, "pub struct Settings {", "\nimpl Settings {"))
    if set(found) != set(FIELDS):
        raise ValueError(f"Launcher Settings fields need review: {set(found) ^ set(FIELDS)}")
    index = read(official, GLASS)
    look = read(official, LOOK)
    language = read(official, LANGUAGE)
    language_codes, _ = section(language, "pub fn code(self)", "\n    }")
    rows = []
    for key, (label_page, label, default, values, cli) in FIELDS.items():
        docs, offset = found[key]
        refs = [anchor("official", BACKEND, source, offset)]
        if key in {"appearance", "theme_accents", "effects", "reduce_motion"}:
            refs.append(anchor("official", LOOK, look, look.index(f'"{key}"') if key != "appearance" else look.index("fn settle")))
        if key == "language":
            refs.append(anchor("official", LANGUAGE, language, language.index("pub enum Language")))
            values = ["system", *re.findall(r'Self::\w+ => "([^"\n]+)"', language_codes)]
        if key == "cache_limit_gib":
            storage_ui = read(official, STORAGE)
            refs.append(anchor("official", STORAGE, storage_ui, storage_ui.index('tr("Cache limit")')))
        if key == "ping_off":
            privacy_ui = read(official, TELEMETRY)
            refs.append(anchor("official", TELEMETRY, privacy_ui, privacy_ui.index("pub const PING_NAME")))
        if label and f'"{label}"' in index:
            refs.append(anchor("official", GLASS, index, index.index(f'"{label}"')))
        row = setting_row("launcher", key, "launcher/settings.json", default, values, refs,
                          FIELD_DESCRIPTIONS.get(key, docs), label, ("Launcher / " + label_page) if label_page else None)
        row["scope"] = "settings" if label else "legacy migration" if key == "appearance" else "config"
        if cli:
            row["aliases"].append(cli if key == "game_args" else f"settings {cli}")
        if key == "while_playing":
            row["aliases"].extend(["minimise", "keep", "open", "off"])
        elif key == "game_architecture":
            row["aliases"].extend(["default", "x86_64", "amd64", "aarch64"])
        if key == "game_architecture":
            row["enabling_conditions"] = "Manual overrides are available on Windows ARM."
        elif key == "desktop_entry":
            row["official_baseline_status"] = "H"
            row["enabling_conditions"] = "Official control appears on Linux only."
        else:
            row["enabling_conditions"] = "Official desktop launcher and applicable page/action."
        tracked_port(row, key)
        rows.append(row)
    coverage[f"official:{BACKEND.as_posix()}"] = list(found)
    coverage[f"official:{LOOK.as_posix()}"] = ["appearance", "theme_accents", "effects", "reduce_motion"]
    effects = read(official, EFFECTS)
    flags = dict((name, (docs, offset)) for name, docs, offset in documented_fields(
        effects, "pub struct Effects {", "\nimpl Default"))
    if set(flags) != set(EFFECT_LABELS):
        raise ValueError(f"Launcher effect flags need review: {set(flags) ^ set(EFFECT_LABELS)}")
    for key, (docs, offset) in flags.items():
        row = setting_row("launcher", f"effects.{key}", "launcher/settings.json", True,
                          "Boolean", [anchor("official", EFFECTS, effects, offset)], docs,
                          EFFECT_LABELS[key], "Launcher / Appearance")
        tracked_port(row, f"effects.{key}")
        rows.append(row)
    coverage[f"official:{EFFECTS.as_posix()}"] = [f"effects.{key}" for key in flags]
    return rows


def glass_controls(official, coverage):
    source = read(official, GLASS)
    body, offset = section(source, "fn glass_settings_sections", "/// About's credit")
    calls = list(re.finditer(r"\.child(?:ren)?\(self\.(\w+)\(", body))
    mapped = {
        "launcher_theme_row": "theme", "glass_accent_row": "accent", "look_slider_row": "glass/blur",
        "performance_row": "performance", "motion_row": "reduce_motion", "effects_rows": "effects",
        "home_background_row": "background", "language_row": "language", "while_playing_row": "while_playing",
        "proxy_row": "proxy", "game_architecture_row": "game_architecture", "game_args_row": "game_args",
        "mods_auto_update_row": "mods_auto_update", "glass_storage": "cache_limit_gib",
    }
    groups = {
        "glass_account": ("Accounts", "Microsoft account", "Opens account sign-in, selection and sign-out controls.",
                          "The host independently supports offline and Microsoft device-code account flows."),
        "update_tiles": ("Updates", "Horizon / Launcher", "Displays current release and update actions.",
                         "About and startup checks show the installed package and available releases."),
        "reset_look_row": ("Appearance", "Reset the launcher's look", "Resets stored appearance controls to launcher defaults.", None),
        "no_ai_row": ("AI", "No AI", "Controls the launcher and games' agent access preference.", None),
        "agents_section": ("AI", "Agents / MCP", "Displays agent client integration commands and settings.", None),
        "credit_row": ("About", "UI made by Xmilon", "Shows the launcher appearance credit.", None),
        "version_row": ("About", "Version", "Shows exact installed game and launcher versions.", "UWP About reports package identity, source commit and architecture."),
        "location_row": ("About", "Data folder", "Shows the launcher data folder and action to open it.", None),
        "collection_section": ("Data & privacy", "Data collection", "Shows telemetry consent, reporting and preview actions.", "UWP has per-service telemetry options and bug-report review."),
    }
    found = {call[1] for call in calls}
    if found != set(mapped) | set(groups):
        raise ValueError(f"Launcher Settings card declarations need review: {found ^ (set(mapped) | set(groups))}")
    rows = []
    for call in calls:
        key = call[1]
        if key not in groups:
            continue
        page, label, purpose, port = groups[key]
        row = setting_row("launcher-ui", key, None, "Action", "Open / invoke",
                          [anchor("official", GLASS, source, offset + call.start())], purpose,
                          label, f"Launcher / {page}", persisted=None)
        row["scope"] = "action"
        tracked_port(row, key)
        if port:
            row.update(uwp_uniform_status="P", uwp_runtime_action=port, root_cause=port)
            row["sources"].append("product:uwp/pregame/README.md:1")
        rows.append(row)
    # Inline settings/actions do not call a named row helper.
    coverage[f"official:{GLASS.as_posix()}"] = sorted(found | {
        "Install new versions automatically", "Delete old versions automatically",
        "Show the Horizon logo at startup", "Logs", "Applications menu",
    })
    marker = body.index('tr("Logs")')
    row = setting_row("launcher-ui", "open-logs", None, "Action", "Open",
                      [anchor("official", GLASS, source, offset + marker)],
                      "Opens the launcher's log folder and nearby crash reports.", "Logs", "Launcher / About", persisted=None)
    row.update(scope="action", owner="MCHorizonUWP", uwp_uniform_status="P",
               uwp_runtime_action="Pregame host includes its own Logs page for private client reports.",
               uwp_ui="Open the UWP shell Logs page.", uwp_persistence=None)
    row["sources"].append("product:uwp/pregame/host/XamlShell.cpp:3958")
    rows.append(row)
    return rows


def consent_switches(official, coverage):
    telemetry = read(official, TELEMETRY)
    consent = read(official, CONSENT)
    shell = read(PRODUCT, SHELL)
    list_source, offset = section(telemetry, "pub const SWITCHES:", "/// Never sent")
    by_kind = {"Perf": ("performance", 0), "Crash": ("crashes", 1),
               "Bug": ("bugs", 2), "Benchmark": ("benchmarks", 3)}
    choices = {key: line for key, _, line in documented_fields(consent, "pub struct Choices {", "impl Choices")}
    switches = list(re.finditer(r'Kind::(\w+),\s*"([^"\n]+)",\s*"([^"\n]+)"', list_source))
    if {switch[1] for switch in switches} != set(by_kind) or set(choices) != {value[0] for value in by_kind.values()}:
        raise ValueError("Launcher telemetry controls and consent fields need review")
    rows = []
    for switch in switches:
        key, product_row = by_kind[switch[1]]
        sources = [anchor("official", TELEMETRY, telemetry, offset + switch.start()),
                   anchor("official", CONSENT, consent, choices[key]),
                   anchor("product", SHELL, shell, shell.index(f"add_toggle({product_row},"))]
        row = setting_row("launcher-consent", f"choices.{key}", "telemetry/consent.json", False,
                          "Boolean", sources, switch[3], switch[2], "Launcher / Data & privacy")
        row.update(uwp_uniform_status="P", owner="MCHorizonUWP",
                   uwp_ui="UWP Data collection has independent choices for each endpoint.",
                   uwp_runtime_action="Corresponding report category is present in both UWP endpoint settings. Exact consent, payload and receipt require independent checks.",
                   uwp_persistence="UWP uses the game bridge's separate consent contracts.")
        rows.append(row)
    coverage[f"official:{TELEMETRY.as_posix()}"] = ["ping_off", *[row["key"] for row in rows]]
    coverage[f"official:{CONSENT.as_posix()}"] = [row["key"] for row in rows]
    return rows


def kebab(name):
    return re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "-", name).lower().replace("_", "-")


def cli_body(source, name):
    match = next(container for container in CLI_CONTAINER.finditer(source) if container[1] == name)
    end = re.search(r"(?m)^\}$", source[match.end():])
    return source[match.end():match.end() + end.start()], match.end()


def prior_docs(source, offset, indent):
    lead = source[:offset].splitlines()
    selected = []
    while lead:
        line = lead.pop()
        if line.startswith(indent + "///"):
            selected.append(line.strip()[4:])
        elif line.startswith(indent + "#[") or line.startswith(indent + "    ") or line == indent + ")]":
            continue
        else:
            break
    return " ".join(reversed(selected))


def cli_flags(body, indent):
    attribute = re.compile(r"(?m)^ {%s}#\[(?:arg|command)\(" % indent)
    for field in ARGUMENT.finditer(body):
        if len(field["indent"]) != indent:
            continue
        declarations = body[:field.start()]
        starts = list(attribute.finditer(declarations))
        tail = declarations[starts[-1].end():].strip() if starts else ""
        args = tail.removesuffix(")]") if tail.endswith(")]") else ""
        if "subcommand" in args:
            continue
        yield field, args


def cli_records(official, coverage):
    source = read(official, CLI)
    nested = {}
    root, root_start = cli_body(source, "Cmd")
    variants = list(VARIANT.finditer(root))
    for index, variant in enumerate(variants):
        stop = variants[index + 1].start() if index + 1 < len(variants) else len(root)
        for command in re.findall(r"action: Option<(\w+Cmd)>", root[variant.end():stop]):
            nested[command] = kebab(variant[1])
    expected = {container[1] for container in CLI_CONTAINER.finditer(source)
                if container[1].endswith("Cmd") and container[1] != "Cmd"}
    if set(nested) != expected:
        raise ValueError(f"Unaccounted launcher subcommand groups: {set(nested) ^ expected}")
    rows = []

    def add(path, body, offset, key, purpose, values, default):
        row = setting_row("launcher-cli", key, "Launcher CLI invocation", default, values,
                          [anchor("official", path, body, offset)], purpose,
                          persisted="Persists only when the command explicitly writes its destination.")
        tracked_port(row, "launcher-cli")
        row.update(scope="cli", uwp_ui="No matching horizon-launcher command surface in AppContainer.",
                   uwp_runtime_action="Official launcher command is absent from the packaged UWP app. Equivalent host and Builder workflows have their own entry points.",
                   uwp_persistence="Invocation is not supplied to the UWP package.")
        rows.append(row)

    def add_fields(path, full, body, offset, prefix, indent):
        for field, args in cli_flags(body, indent):
            name = field["name"]
            dtype = field["type"]
            explicit = re.search(r'\blong\s*=\s*"([^"\n]+)"', args)
            flag = ("--" + (explicit[1] if explicit else kebab(name))) if re.search(r"\blong\b", args) else f"<{kebab(name)}>"
            choices = re.search(r"value_parser\s*=\s*(\[[^\n]+?\]|[A-Z_][A-Z0-9_]+)", args)
            default = re.search(r'\bdefault_value(?:_t)?\s*=\s*("[^"]*"|[^,\s]+)', args)
            effective = default[1].strip('"') if default else (False if dtype == "bool" else "Omitted" if dtype.startswith(("Option<", "Vec<")) else "Required")
            docs = prior_docs(full, offset + field.start(), " " * indent)
            constraints = " ".join(args.split())
            purpose = docs or f"Passes {flag} as the {name} argument to the official launcher command."
            detail = choices[1] if choices else dtype
            if constraints:
                detail += f" ({constraints})"
            add(path, full, offset + field.start(), f"{prefix} {flag}".strip(), purpose, detail, effective)

    top, start = cli_body(source, "Cli")
    add_fields(CLI, source, top, start, "", 4)
    for name, prefix in {"Cmd": "", **nested}.items():
        body, start = cli_body(source, name)
        items = list(VARIANT.finditer(body))
        for index, item in enumerate(items):
            end = items[index + 1].start() if index + 1 < len(items) else len(body)
            command = f"{prefix} {kebab(item[1])}".strip()
            docs = prior_docs(source, start + item.start(), "    ")
            add(CLI, source, start + item.start(), command,
                docs or "Runs the named official launcher command.", "Subcommand", "Explicit invocation")
            if item[1] == "DeveloperInstall" and name == "Cmd":
                dev = read(official, DEVELOPER)
                part, offset = cli_body(dev, "DeveloperInstall")
                before = len(rows)
                add_fields(DEVELOPER, dev, part, offset, command, 4)
                coverage[f"official:{DEVELOPER.as_posix()}"] = [row["key"] for row in rows[before:]]
            field_body = body[item.end():end]
            add_fields(CLI, source, field_body, start + item.end(), command, 8)
            if inline := re.match(r"\s*\{ ([a-z][a-z0-9_]*): ([^}]+?) \},", field_body):
                kind, dtype = inline.groups()
                add(CLI, source, start + item.end() + inline.start(1), f"{command} <{kebab(kind)}>",
                    f"Supplies the launcher's {kind} argument.", dtype, "Omitted" if dtype.startswith("Option<") else "Required")
    key_block, _ = section(source, "const SETTING_KEYS:", "fn on_off")
    registered = set(re.findall(r'"([^"\n]+)"', key_block))
    mapped = {field[-1] for field in FIELDS.values()} - {None, "game-args"}
    if registered != mapped:
        raise ValueError(f"Launcher CLI settings aliases need review: {registered ^ mapped}")
    coverage[f"official:{CLI.as_posix()}"] = [row["key"] for row in rows if not row["key"].startswith("developer-install ")]
    coverage[f"official:{CLI.as_posix()}"] += [f"settings key {key}" for key in sorted(registered)]
    if len({row["key"] for row in rows}) != len(rows):
        raise ValueError("Duplicate launcher CLI declarations")
    return rows


def inventory(official, _uwp):
    coverage = {}
    rows = settings(official, coverage)
    rows.extend(glass_controls(official, coverage))
    rows.extend(consent_switches(official, coverage))
    rows.extend(cli_records(official, coverage))
    return rows, coverage
