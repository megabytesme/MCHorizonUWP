"""Extract Horizon's vanilla options, controls, and controller settings."""

import json
import re
from collections import defaultdict
from pathlib import Path

RENDER = "crates/horizon-client-render/src/"
INPUT = "crates/horizon-client-input/src/"
GAME = "crates/horizon-client/src/game/"
OPTIONS = RENDER + "options_file.rs"
OPTIONS_UI = RENDER + "options_ui.rs"
SETTINGS = RENDER + "settings.rs"
KEYS = INPUT + "keybinds.rs"
SOUND = "crates/horizon-client-core/src/sound_source.rs"
PAD = INPUT + "gamepad.rs"
CONTROLLER_UI = RENDER + "controller_ui.rs"
RADIAL = INPUT + "radial.rs"
LAUNCH = GAME + "runtime/launch_options.rs"
ENGLISH = "crates/horizon-client/assets/horizon/lang/en_us.json"

# Some controls access a typed Settings field and therefore have no file_key arm.
OPTION_CONTROLS = dict(line.split() for line in """
ao SmoothLighting
biomeBlendRadius BiomeBlend
chunkSectionFadeInTime ChunkFade
cutoutLeaves CutoutLeaves
enableVsync Vsync
entityDistanceScaling EntityDistance
entityShadows EntityShadows
forceUnicodeFont ForceUnicode
japaneseGlyphVariants JapaneseGlyphs
fov Fov
preferredGraphicsBackend GraphicsBackend
graphicsPreset Preset
prioritizeChunkUpdates ChunkBuilder
fullscreen Fullscreen
exclusiveFullscreen ExclusiveFullscreen
fullscreenResolution ExclusiveMode
gamma Gamma
guiScale GuiScale
maxAnisotropyBit MaxAnisotropy
textureFiltering TextureFiltering
maxFps MaxFps
improvedTransparency ImprovedTransparency
inactivityFpsLimit InactivityFps
mipmapLevels Mipmaps
particles Particles
reducedDebugInfo ReducedDebugInfo
renderClouds Clouds
cloudRange CloudRange
renderDistance RenderDistance
simulationDistance SimulationDistance
vignette Vignette
weatherRadius WeatherRadius
autoJump AutoJump
autoSuggestions AutoSuggestions
chatColors ChatColors
chatLinks ChatLinks
chatLinksPrompt ChatLinksPrompt
discrete_mouse_scroll DiscreteScroll
invertXMouse InvertX
invertYMouse InvertY
showSubtitles ShowSubtitles
bobView ViewBobbing
toggleCrouch ToggleCrouch
toggleSprint ToggleSprint
toggleAttack ToggleAttack
toggleUse ToggleUse
sprintWindow SprintWindow
mouseSensitivity Sensitivity
highContrast HighContrast
chatVisibility ChatVisibility
chatOpacity ChatOpacity
chatLineSpacing ChatLineSpacing
textBackgroundOpacity TextBackgroundOpacity
chatHeightFocused ChatHeightFocused
chatHeightUnfocused ChatHeightUnfocused
chatScale ChatScale
chatWidth ChatWidth
notificationDisplayTime NotificationDisplayTime
mainHand MainHand
attackIndicator AttackIndicator
mouseWheelSensitivity WheelSensitivity
allowCursorChanges AllowCursorChanges
showAutosaveIndicator Autosave
menuBackgroundBlurriness MenuBlur
""".splitlines() if line)

PAGE_LABELS = {
    "Main": "Options", "Video": "Video Settings (legacy control route)",
    "Sound": "Music & Sounds", "Skin": "Skin Customization",
    "Online": "Online Options", "Controls": "Controls", "Mouse": "Mouse Settings",
    "Chat": "Chat Settings", "Accessibility": "Accessibility Settings",
    "Font": "Font Settings", "Language": "Language", "Debug Options": "F3+F6 Debug Options",
    "GraphicsGeneral": "Graphics > General", "GraphicsQuality": "Graphics > Quality",
    "GraphicsPerformance": "Graphics > Performance", "GraphicsAdvanced": "Graphics > Advanced",
    "GraphicsHdr": "Graphics > HDR", "GraphicsShaders": "Graphics > Shaders",
}
PAGES = ("Video", "Sound", "Skin", "Online", "Controls", "Mouse", "Chat", "Accessibility", "Font")

EFFECTS = {
    "ao": "Smooth or flatten corner shading on block faces.",
    "biomeBlendRadius": "Change the extent of biome color blending.",
    "chunkSectionFadeInTime": "Change how long newly drawn chunk sections fade in.",
    "enableVsync": "Change the requested synchronization of frame presentation.",
    "entityDistanceScaling": "Change the view range of rendered entities.",
    "entityShadows": "Draw or suppress entity ground shadows.",
    "lang": "Change translated menus, tooltips and other localized text.",
    "soundDevice": "Select a different audio output endpoint.",
    "showSubtitles": "Show sound event captions and direction indicators.",
    "directionalAudio": "Request directional sound processing.",
    "musicToast": "Change where the current track is announced.",
    "musicFrequency": "Change music selection cadence.",
    "narrator": "Speak the selected subset of chat or system messages.",
    "narratorHotkey": "Allow the narrator keyboard shortcut.",
    "fov": "Change the camera's field of view.",
    "gamma": "Change perceived world brightness.",
    "forceUnicodeFont": "Use the forced Unicode font face and its GUI scaling rules.",
    "japaneseGlyphVariants": "Choose the Japanese glyph variant where supported.",
    "renderDistance": "Change the extent of the visible world.",
    "simulationDistance": "Change the range of local-server simulation.",
    "renderClouds": "Change whether clouds are drawn and their draw mode.",
    "cloudRange": "Change cloud draw extent.",
    "maxFps": "Change the application frame-rate limit.",
    "particles": "Change the amount of particle detail.",
    "mipmapLevels": "Change the number of generated texture mip levels.",
    "fullscreenResolution": "Choose the display mode for exclusive fullscreen.",
    "advancedItemTooltips": "Show technical details in item tooltips.",
    "pauseOnLostFocus": "Pause eligible single-player sessions when focus is lost.",
    "wasm.compile_timeout_secs": "Limit startup Wasm compilation time.",
    "resourcePacks": "Apply the selected resource packs in their saved order.",
    "incompatibleResourcePacks": "Retain the accepted incompatible resource-pack selections.",
    "modelPart": "Show or hide this layer of the player skin.",
}

LEGACY = {
    "graphicsPreset": ["graphicsMode", "fancyGraphics"],
    "key_key.swapOffhand": ["key_key.swapHands"],
    "musicToast": ["showNowPlayingToast"],
}

PAD_CONTROLS = dict(line.split() for line in """
enabled PadEnabled
vibration PadVibration
playInBackground PadPlayInBackground
adaptiveTriggers PadAdaptiveTriggers
impulseTriggers ImpulseTriggers
blockVibration BlockVibration
splitVibration SplitVibration
lightbar PadLightbar
lookSensitivity PadLookSensitivity
invertX PadInvertX
invertY PadInvertY
moveDeadzone PadMoveDeadzone
lookDeadzone PadLookDeadzone
triggerThreshold PadTriggerThreshold
toggleSneak PadToggleSneak
craftOnServers PadCraftOnServers
glyphs PadGlyphs
nintendoLetters PadNintendoButtons
keyboard PadKeyboard
gameplayGuide PadGameplayGuide
pointerSpeed PadPointerSpeed
scrollSpeed PadScrollSpeed
repeatDelay PadRepeatDelay
repeatInterval PadRepeatInterval
radialMode PadRadialMode
radialStick PadRadialStick
""".splitlines() if line)

def source(root, path):
    return (root / path).read_text(encoding="utf-8")


def where(src, offset):
    return src.count("\n", 0, offset) + 1


def anchor(path, src, pattern):
    match = re.search(pattern, src, re.MULTILINE)
    if match is None:
        raise ValueError(f"{path}: cannot find {pattern}")
    return f"{path}:{where(src, match.start())}"


def section(src, start, finish):
    first = src.index(start)
    return src[first:src.index(finish, first + len(start))], first


def array(src, name):
    match = re.search(fr'\bconst {name}: [^\n]+? = &?\[(.*?)\n?\];', src, re.S)
    if not match:
        raise ValueError(f"Missing array {name}")
    return match[1], match.start(1)


def strings(src):
    return re.findall(r'"([^"]*)"', src)


def literal(value):
    value = value.strip()
    try:
        return json.loads(value)
    except ValueError:
        return value


def spec(kind, args, src):
    range_match = re.fullmatch(r"(Int|LazyInt|Scaled|Id)\(([^)]*)\)", kind)
    if range_match:
        name, value = range_match.groups()
        nums = [
            literal(item.strip().replace("MAX_RENDER_DISTANCE", re.search(
                r"const MAX_RENDER_DISTANCE: i32 = (\d+)", src
            )[1]))
            for item in value.split(",")
        ]
        if name == "Int":
            return {"type": "integer", "min": nums[0], "max": nums[1]}
        if name == "LazyInt":
            return {"type": "integer", "min": nums[0], "max": "runtime"}
        if name == "Id":
            return {"type": "integer enum", "choices": list(range(nums[0]))}
        min_step, max_step, divisor, wire_min, wire_max = nums
        return {"type": "number", "min": min_step / divisor, "max": max_step / divisor,
                "step": 1 / divisor, "wire_min": wire_min, "wire_max": wire_max}
    enum_match = re.fullmatch(r"Named\((\w+)\)", kind)
    options = enum_match[1] if enum_match else {"Clouds": "CLOUDS", "Tutorial": "TUTORIAL_STEPS"}.get(kind)
    if options:
        return {"type": "enum", "choices": strings(array(src, options)[0])}
    if kind in {"Unit", "UnitSquare", "UnitSquareImplicit"}:
        return {"type": "number", "min": 0, "max": 1, "codec": kind}
    if kind in {"Bool", "PB"}:
        return {"type": "boolean"}
    if kind in {"AnyStr", "PS"}:
        return {"type": "string"}
    if kind == "List":
        return {"type": "string list"}
    if kind == "PI":
        return {"type": "integer"}
    if kind == "FrameLimit":
        return {"type": "integer", "min": 10, "max": 260, "step": 10}
    if kind == "Fov":
        return {"type": "integer", "min": 30, "max": 110}
    if kind == "WheelSensitivity":
        return {"type": "number", "min": 0.01, "max": 10, "step_count": 300}
    raise ValueError(f"Unrecognized options.txt declaration {kind}: {args}")


def options_defs(root):
    src = source(root, OPTIONS)
    table, offset = section(src, "let mut t = vec![", "\n    ];")
    pattern = (
        r'^ {8}(?P<form>b|i|u|n|id|pb|def)\(\s*"(?P<key>[^"]+)",[ \t]*'
        r'(?:(?P<args>[^\n]+?)\),|(?P<multiline>.*?)\n {8}\),)$'
    )
    matches = list(re.finditer(pattern, table, re.M | re.S))
    table_keys = re.findall(r'(?:b|i|u|n|id|pb|def)\(\s*"([^"]+)"', table)
    if [m["key"] for m in matches] != table_keys:
        raise ValueError("Unparsed options.txt table rows")
    defs = {}
    for m in matches:
        key = m["key"]
        form = m["form"]

        args = (m["args"] or m["multiline"]).strip().rstrip(",")
        if form in {"b", "pb", "u"}:
            default = args
        else:
            args, default = args.rsplit(",", 1)
        if form == "def":
            rust_kind = args.strip()
            kind = rust_kind[2:-1] if rust_kind.startswith("O(") else rust_kind
            default = re.fullmatch(r"V::\w+\((.*)\)", default.strip())[1]
            if default == "String::new()":
                default = '""'
            elif default == "Vec::new()":
                default = "[]"
            default = default.removesuffix(".into()")
        else:
            kind = {"b": "Bool", "pb": "PB", "u": "Unit", "i": "Int",
                    "n": "Named", "id": "Id"}[form]
            if form in {"i", "n", "id"}:
                kind = f"{kind}({args.strip()})"
        value = literal(default)
        values = spec(kind, args, src)
        if values["type"] == "enum":
            value = values["choices"][value]
        defs[key] = (values, value, f"{OPTIONS}:{where(src, offset + m.start())}", None)

    key_src = source(root, KEYS)
    mappings, offset = array(key_src, "MAPPINGS")
    key_matches = list(re.finditer(r'\(\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)",?\s*\)', mappings))
    if len(key_matches) != len(strings(mappings)) // 3:
        raise ValueError("Unparsed key mapping")
    generated = {"key_": (KEYS, []), "soundCategory_": (SOUND, []), "modelPart_": (OPTIONS, [])}
    for m in key_matches:
        label, default, group = m.groups()
        key = f"key_{label}"
        generated["key_"][1].append((key, where(key_src, offset + m.start()), default, (label, group)))
    for prefix, const, path, content in (
        ("soundCategory_", "SOUND_SOURCES", SOUND, source(root, SOUND)),
        ("modelPart_", "MODEL_PARTS", OPTIONS, src),
    ):
        items, offset = array(content, const)
        for m in re.finditer(r'"([^"]+)"', items):
            generated[prefix][1].append((prefix + m[1], where(content, offset + m.start()), True, None))
    for prefix, (path, declarations) in generated.items():
        for key, line, default, label in declarations:
            values = {"type": "physical key identifier"} if prefix == "key_" else {"type": "boolean"}
            if prefix == "soundCategory_":
                values, default = {"type": "number", "min": 0, "max": 1}, 1.0
            defs[key] = values, default, f"{path}:{line}", label

    parse, offset = section(src, "pub fn parse(text", "fn try_apply(")
    special_keys = [
        (key, offset + m.start()) for m in re.finditer(r'\.get\("([^"]+)"\)', parse) for key in [m[1]]
    ]
    version_path = "crates/horizon-core/src/version.rs"
    version = re.search(r"pub const DATA_VERSION: i32 = (\d+)", source(root, version_path))
    special = {
        "wasm.compile_timeout_secs": ({"type": "raw string", "consumer": "Wasm startup"}, None),
        "version": ({"type": "integer", "load_default_if_absent": 0}, int(version[1])),
        "fullscreenResolution": ({"type": "display mode string"}, None),
    }
    if {key for key, _ in special_keys} != special.keys():
        raise ValueError(f"Unmapped raw options.txt keys: {special_keys}")
    for key, mark in special_keys:
        values, default = special[key]
        defs[key] = values, default, f"{OPTIONS}:{where(src, mark)}", None
    return defs


def controls(root):
    src = source(root, OPTIONS_UI)
    caption_body, caption_offset = section(src, "fn caption(self)", "fn kind(self")
    captions = {m[1]: (m[2], f"{OPTIONS_UI}:{where(src, caption_offset + m.start())}")
                for m in re.finditer(r'Opt::(\w+)\s*=>\s*"([^"]+)"', caption_body)}
    available, _ = section(src, "fn available(self)", "fn file_key(self)")
    disabled = set(re.findall(r"Opt::(\w+)", available))
    access, access_offset = array(src, "ACCESSIBILITY_OPTIONS")
    mouse, mouse_offset = array(src, "MOUSE_OPTIONS")
    control, control_offset = section(src, "fn vanilla_control_options()", "const MOUSE_OPTIONS")
    rows, offset = section(src, "fn list_rows(", "fn heading_row(")
    page_bits = list(re.finditer(r"^ {12}Page::(" + "|".join(PAGES) + r") =>", rows, re.M))
    pages = defaultdict(dict)

    def collect(page, file, contents, part, start):
        for m in re.finditer(r"Opt::(\w+)", part):
            pages[m[1]][page] = f"{file}:{where(contents, start + m.start())}"

    for m in page_bits:
        end = re.search(r"^ {12}(?:Page::|_ =>)", rows[m.end():], re.M)
        collect(m[1], OPTIONS_UI, src, rows[m.end():m.end() + end.start()], offset + m.end())
    collect("Accessibility", OPTIONS_UI, src, access, access_offset)
    collect("Controls", OPTIONS_UI, src, control, control_offset)
    collect("Mouse", OPTIONS_UI, src, mouse, mouse_offset)
    debug = source(root, DEBUG_UI)
    pages["DebugGuiScale"]["Debug Options"] = anchor(DEBUG_UI, debug, r'^    /// `debugGuiScale`')
    captions["DebugGuiScale"] = ("options.guiScale", pages["DebugGuiScale"]["Debug Options"])
    layout_path = RENDER + "options_ui/layout.rs"
    layout = source(root, layout_path)
    pages["Fov"]["Main"] = anchor(layout_path, layout, r"option_widget\(s, Opt::Fov,")
    pages["ResourcePacks"]["Resource Packs"] = anchor(OPTIONS_UI, src, r'Page::Packs => "resourcePack.title"')
    pages["Language"]["Language"] = anchor(OPTIONS_UI, src, r"Page::Language => \"options.language.title\"")
    captions.update(Language=("options.language.title", pages["Language"]["Language"]),
                    ResourcePacks=("options.resourcepack", pages["ResourcePacks"]["Resource Packs"]))

    graphics_path = RENDER + "graphics_ui.rs"
    graphics = source(root, graphics_path)
    graph_rows, graph_offset = section(graphics, "pub fn rows()", "\nconst BLUE_MAP_RADII")
    graph_pages = list(re.finditer(r"\bPage::(General|Quality|Performance|Advanced|Hdr|Shaders),", graph_rows))
    for i, m in enumerate(graph_pages):
        end = graph_pages[i + 1].start() if i + 1 < len(graph_pages) else len(graph_rows)
        collect("Graphics" + m[1], graphics_path, graphics, graph_rows[m.end():end], graph_offset + m.end())

    file_keys, _ = section(src, "fn file_key(self)", "fn caption(self)")
    explicit = {m[2]: m[1] for m in re.finditer(r'Opt::(\w+)\s*=>\s*"([^"]+)"', file_keys)}
    mapped = OPTION_CONTROLS | explicit | {
        "debugGuiScale": "DebugGuiScale", "lang": "Language", "resourcePacks": "ResourcePacks", "incompatibleResourcePacks": "ResourcePacks",
    }
    return mapped, pages, captions, disabled


def row(key, storage, values, default, refs, label=None, titles=(), scope="setting"):
    return {
        "id": f"{storage}:{key}", "key": key, "aliases": LEGACY.get(key, []),
        "official_ui_label": label, "official_ui_page": list(titles), "storage": storage,
        "values": values, "default": default, "scope": scope,
        "official_baseline_status": "C",
        "official_windows_behavior": "The source declares and reads this value.",
        "expected_visible_effect": EFFECTS.get(key),
        "uwp_ui": "No control traced" if not titles else "Source control found; selectability untested",
        "uwp_persistence": "Unverified", "uwp_runtime_action": "Unverified",
        "enabling_conditions": [], "sources": refs, "evidence": "Source only",
        "test_method": "Change the value in an isolated profile, restart, and observe the stated behavior.",
        "root_cause": None, "recommendation": "Verify the consumer and outcome in a signed build.",
    }


def localization(root, label):
    # Java's vanilla labels come from the legally prepared resource set, which is not part of this pin.
    if label is None:
        return None
    bundled = json.loads(source(root, ENGLISH))
    return bundled.get(label, label)


def ref(side, path, text, pattern):
    return side + ":" + anchor(path, text, pattern)


def official_profile(record, status, reason):
    record["official_baseline_status"] = status
    record["official_windows_behavior"] = reason


def parsed_ui_effects(root):
    """Read the descriptions displayed alongside vanilla graphics controls."""
    src = source(root, RENDER + "graphics_ui.rs")
    graphic, _ = section(src, "pub fn rows()", "\nconst BLUE_MAP_RADII")
    by_key, _, _, _ = controls(root)
    by_control = {control: key for key, control in by_key.items()}
    pattern = r'v\(\s*Opt::(\w+),\s*\w+,\s*"([^"]+)"'
    return {by_control[m[1]]: m[2] for m in re.finditer(pattern, graphic) if m[1] in by_control}


def add_option_runtime_evidence(official, uwp, record):
    key = record["key"]
    runtime = OPTIONS_RUNTIME.get(key)
    if runtime:
        path, pattern, effect = runtime
        record["expected_visible_effect"] = effect
        record["official_windows_behavior"] = effect
        record["sources"].append(ref("official", path, source(official, path), pattern))
        port_source = source(uwp, path)
        if re.search(pattern, port_source, re.M):
            record["sources"].append(ref("uwp", path, port_source, pattern))
            record["uwp_runtime_action"] = "Source consumer present. " + effect
    elif key in OPTIONS_WITHOUT_CONSUMER:
        effect = OPTIONS_WITHOUT_CONSUMER[key]
        record["expected_visible_effect"] = effect
        record["official_windows_behavior"] = effect
        record["uwp_runtime_action"] = "No consuming path identified beyond configuration persistence."
        record["recommendation"] = "Confirm whether this compatibility key should still be retained."
    elif key in OPTIONS_UNAVAILABLE_EFFECTS:
        record["expected_visible_effect"] = OPTIONS_UNAVAILABLE_EFFECTS[key]

    if key == "allowCursorChanges":
        window = "third_party/winit/src/platform_impl/uwp/window.rs"
        record["sources"].append(ref("uwp", window, source(uwp, window), r"fn set_cursor\("))
        record["uwp_runtime_action"] = "The game forwards the chosen icon to UWP Winit, whose set_cursor implementation does nothing."
        record["root_cause"] = "The AppContainer Winit cursor icon hook is a stub."
        record["uwp_uniform_status"] = "S"
        record["recommendation"] = "Wire the native cursor icon hook and test text and link hovers."
    if key == "onlyShowSecureChat":
        path = GAME + "network/remote/mod.rs"
        secure_source = r'let content = if self\.settings\.file\.bool\("onlyShowSecureChat"'
        for side, root in (("official", official), ("uwp", uwp)):
            record["sources"].append(ref(side, path, source(root, path), secure_source))
        official_profile(record, "P", "The UI row is disabled. The remote chat listener still reads the saved value, drops NotSecure messages and selects the signed body.")
        record["uwp_uniform_status"] = "P"
        record["uwp_runtime_action"] = "The UI row is disabled. The patched remote chat listener retains both configuration reads."
        record["root_cause"] = "The UI availability check disables the row despite the presence of its consumer."
        record["enabling_conditions"] = ["Remote player chat", "Value set in options.txt"]
        record["recommendation"] = "Verify authenticated chat and evaluate enabling the UI control."
        record["test_method"] = "Join a compatible authenticated server using two isolated profiles with distinct values. Compare signed, altered and insecure chat delivery."
    if key == "preferredGraphicsBackend":
        record["uwp_runtime_action"] = "Reads the legacy Auto/OpenGL/Vulkan values. The UWP host and launch configuration select the actual AppContainer renderer."
    if key in {"chatDelay", "chatVisibility"}:
        record["enabling_conditions"].append("Incoming chat")
    if key == "rotateWithMinecart":
        record["enabling_conditions"].append("Minecart improvements enabled")
    if key == "highContrast":
        record["enabling_conditions"].append("Built-in high-contrast pack available")


def option_rows(official, uwp, coverage):
    ods, uds = options_defs(official), options_defs(uwp)
    by_key, pages, captions, unavailable = controls(official)
    ukeys, upages, _, uunavailable = controls(uwp)
    ui = source(official, OPTIONS_UI)
    port_ui = source(uwp, OPTIONS_UI)
    ui_effects = parsed_ui_effects(official)
    mappings = {}
    for key in ods:
        if key.startswith("soundCategory_"):
            mappings[key] = "Volume"
        elif key.startswith("modelPart_"):
            mappings[key] = "ModelPart"
        elif key.startswith("key_"):
            mappings[key] = "KeyBinding"
    mapping = by_key | mappings
    known_other_store = {"SkinLayers3d"}
    unlinked = set(pages) - set(mapping.values()) - known_other_store
    if unlinked:
        raise ValueError(f"Unmapped vanilla UI controls: {sorted(unlinked)}")

    rows = []
    for key, (values, default, definition, dynamic) in ods.items():
        control = mapping.get(key)
        label = captions.get(control, (None, None))[0]
        display_pages = pages.get(control, {})
        refs = ["official:" + definition]
        refs.extend("official:" + place for place in display_pages.values())
        if dynamic:
            label, category = dynamic
            display_pages = {f"Key Binds > {category}": anchor(
                OPTIONS_UI, ui, r"fn bind_key\("
            )}
            refs.extend("official:" + place for place in display_pages.values())
        elif control == "Volume":
            label = key.replace("soundCategory_", "soundCategory.")
            display_pages = pages["Volume"]
            refs.append(ref("official", OPTIONS_UI, ui, r'format!\("soundCategory\.\{\}"'))
        elif control == "ModelPart":
            label = key.replace("modelPart_", "options.modelPart.")
            display_pages = pages["ModelPart"]
        refs.extend(["official:" + captions[control][1]] if control in captions and captions[control][1] else [])
        ucontrol = ukeys.get(key, mappings.get(key))
        if key in uds:
            refs.append("uwp:" + uds[key][2])
        refs.extend("uwp:" + place for place in upages.get(ucontrol, {}).values())
        if dynamic and key in uds:
            refs.append(ref("uwp", OPTIONS_UI, port_ui, r"fn bind_key\("))
        record = row(key, "options.txt", values, default, refs,
                     localization(official, label),
                     (PAGE_LABELS.get(p, p) for p in display_pages),
                     scope="config" if not display_pages else "setting")
        record["official_ui_label_key"] = label
        if key in uds:
            record["uwp_persistence"] = "Source parser and serializer share the declaration."
            if uds[key][:2] != (values, default):
                record["uwp_values"], record["uwp_default"] = uds[key][:2]
        if dynamic:
            record["expected_visible_effect"] = f"Trigger {label} with the selected key or mouse button."
            record["official_windows_behavior"] = "Input is translated through the shared key mapping."
            record["uwp_runtime_action"] = "Shared key mapping is declared. Exercise the bound action independently."
            record["test_method"] = "Bind a spare key, exercise the action in the applicable game state, then restart and repeat."
        elif key.startswith("soundCategory_"):
            category = key.removeprefix("soundCategory_")
            record["expected_visible_effect"] = f"Change the {category} audio gain."
            record["test_method"] = f"Play a reproducible {category} sound, compare RMS at 0%, 50% and 100%, and restart."
            for side, root in (("official", official), ("uwp", uwp)):
                record["sources"].append(ref(side, SETTINGS, source(root, SETTINGS), r"pub fn final_volume"))
            record["official_windows_behavior"] = "The mixer uses the category gain and master gain."
            record["uwp_runtime_action"] = "The same gain calculation is present. Signed non-silent output alone leaves each slider unverified."
        elif key.startswith("modelPart_"):
            record["expected_visible_effect"] = EFFECTS["modelPart"]
            record["official_windows_behavior"] = "The enabled layer is encoded into the client model-customization mask."
            for side, root in (("official", official), ("uwp", uwp)):
                record["sources"].append(ref(side, OPTIONS, source(root, OPTIONS), r"pub fn model_customisation"))
        elif display_pages:
            record["official_windows_behavior"] = "The source declares a control and setting. Per-key runtime trace is pending."
        if control in unavailable or key == "ctrlClickEmulatesRightClick":
            explanation = ("Control appears only on macOS." if key == "ctrlClickEmulatesRightClick" else
                           "A disabled placeholder persists the value. The official source documents the missing consumer.")
            status = "H" if key == "ctrlClickEmulatesRightClick" else "S"
            official_profile(record, status, explanation)
            record["uwp_ui"] = "Hidden by a platform gate" if status == "H" else "Visible and disabled"
            record["uwp_runtime_action"] = explanation
            record["root_cause"] = explanation
            record["enabling_conditions"] = ["macOS"] if status == "H" else ["A consuming system must be implemented."]
            if ucontrol in uunavailable or status == "H":
                record["uwp_uniform_status"] = status

        if key in ui_effects and not record["expected_visible_effect"]:
            record["expected_visible_effect"] = ui_effects[key]
        if key in {"narrator", "narratorHotkey"}:
            speech = "crates/horizon-narrator/src/speech.rs"
            record["sources"].append(ref("uwp", speech, source(uwp, speech), r'return Err\("Narrator speech'))
            desktop = source(official, speech)
            record["sources"].append(ref("official", speech, desktop, r"fn open\("))
            record["official_windows_behavior"] = "Desktop Windows selects SAPI and enables narration if the speech provider opens."
            if key == "narrator":
                record["uwp_ui"] = "Visible and disabled during normal operation"
            record["uwp_runtime_action"] = "AppContainer speech initialization explicitly returns unavailable."
            record["root_cause"] = "The port has no AppContainer speech implementation and excludes desktop SAPI."
            record["recommendation"] = "Implement a supported speech provider and verify actual speech for all four modes."
            record["enabling_conditions"] = ["A working speech provider"]
            record["uwp_uniform_status"] = "U"
        if key == "soundDevice":
            audio = RENDER + "audio.rs"
            a = source(uwp, audio)
            record["sources"].append(ref("official", audio, source(official, audio), r"pub fn output_devices"))
            record["sources"].append(ref("uwp", audio, a, r'#\[cfg\(not\(feature = "audio"\)\)\]\n\w'))
            record["sources"].append(ref("uwp", audio, a, r"fn open_named\(mixer: Arc<Mutex<Mixer>>, _target: &str\)"))
            record["sources"].extend(["product:uwp/game-bridge/Cargo.toml:20", "product:tools/build_nightly_runtime.ps1:54"])
            record["official_windows_behavior"] = "Cycles distinct CPAL endpoint names and default; falls back to default if opening the requested endpoint fails."
            record["uwp_ui"] = "Visible; only the default entry is returned by the packaged audio configuration"
            record["uwp_runtime_action"] = "The UWP-only XAudio2 path enumerates no selectable endpoints through this control."
            record["root_cause"] = "The endpoint list calls the desktop CPAL feature; that feature is disabled for the UWP audio bridge."
            record["recommendation"] = "Implement endpoint enumeration, routing, and hotplug testing for the UWP audio path."
            record["uwp_uniform_status"] = "P"
        if key in {"fullscreen", "exclusiveFullscreen", "fullscreenResolution"}:
            window = INPUT + "window.rs"
            record["sources"].append(ref("official", window, source(official, window), r"pub fn fullscreen_request"))
            port_window = "third_party/winit/src/platform_impl/uwp/window.rs"
            port_module = "third_party/winit/src/platform_impl/uwp/mod.rs"
            record["sources"].append(ref("uwp", port_window, source(uwp, port_window), r"fn set_fullscreen"))
            record["sources"].append(ref("uwp", port_module, source(uwp, port_module), r"fn video_modes"))
            record["sources"].append("product:uwp/host/App.cpp:974")
            record["official_windows_behavior"] = "Requests Winit borderless or exclusive fullscreen. An unusable exclusive mode falls back to borderless."
            record["uwp_runtime_action"] = "The UWP Winit backend returns no exclusive modes and discards fullscreen requests. The host independently requests phone-shell fullscreen on startup."
            record["root_cause"] = "The game-facing UWP windowing API has no fullscreen transition or mode enumeration."
            record["uwp_uniform_status"] = "S"
            record["recommendation"] = "Implement or disable the in-game window controls. Verify actual monitor mode transitions."
            if key == "fullscreenResolution":
                record["enabling_conditions"] = ["An enumerated video mode and exclusive fullscreen"]
                record["uwp_ui"] = "Source control present; no video modes are available for selection"
        add_option_runtime_evidence(official, uwp, record)
        if key == "maxAnisotropyBit":
            record["enabling_conditions"] = ["Anisotropic texture filtering"]
        if "Video" in display_pages:
            record["enabling_conditions"].append("The legacy Video Settings layout is exposed through horizon-ctl.")
        rows.append(record)
        coverage["official:" + definition.rsplit(":", 1)[0]].append(key)
        for name, place in display_pages.items():
            coverage["official:" + place.rsplit(":", 1)[0]].append(f"{name}/{key}")
        if key in uds:
            coverage["uwp:" + uds[key][2].rsplit(":", 1)[0]].append(key)
        for name, place in upages.get(ucontrol, {}).items():
            coverage["uwp:" + place.rsplit(":", 1)[0]].append(f"{name}/{key}")
    return rows


def enum_names(src, name):
    body, _ = section(src, f"impl {name} {{", "\n}\n")
    return dict(re.findall(fr'(?:Self|{name})::(\w+) => "([^"]+)"', body))


def controller_defs(root):
    src = source(root, PAD)
    radial_file = source(root, INPUT + "gamepad/radial_file.rs")
    defaults_src, _ = section(src, "impl Default for PadSettings", "fn bool_text(")
    defaults = dict(re.findall(r"^ {12}(\w+): (.+),$", defaults_src, re.M))
    parse, offset = section(src, "pub fn parse(text: &str) -> Self", "pub fn serialize(&self)")
    parse, match_offset = section(parse, "match key.trim() {", "// An opener")
    offset += match_offset
    arms = list(re.finditer(r'^ {16}"(\w+)" => (.*?)(?=^ {16}(?:"|\w+ =>|\w+ if)|\Z)', parse, re.M | re.S))
    results = {}
    for arm in arms:
        key, code = arm.groups()
        if key == "bindLayout":
            value = int(re.search(r"const BIND_LAYOUT: u32 = (\d+)", src)[1])
            values = {"type": "integer", "min": 0, "meaning": "controller migration version"}
        else:
            field, op = re.search(r"s\.(\w+) = (\w+)", code).groups()
            value = defaults[field]
            if op == "flag":
                values = {"type": "boolean"}
                value = False if value.startswith("cfg!") else literal(value)
            elif op in {"bounded", "unit"}:
                bound = re.search(r"(?:bounded\(s\.\w+, |\.clamp\()([.\d]+), ([.\d]+)\)", code)
                cap = re.search(r"\.min\(([.\d]+)\)", code)
                lo, hi = [float(v) for v in bound.groups()] if bound else (0, float(cap[1]) if cap else 1)
                values = {"type": "number", "min": lo, "max": hi}
                value = literal(value)
            else:
                enum_source = radial_file if op in {"RadialMode", "RadialStick"} else src
                names = enum_names(enum_source, op)
                values = {"type": "enum", "choices": list(names.values())}
                value = names[value.rsplit("::", 1)[1]]
        results[key] = (values, value, f"{PAD}:{where(src, offset + arm.start())}", None)
    unknown = set(results) - {"bindLayout", "swapFaceButtons"} - PAD_CONTROLS.keys()
    if unknown:
        raise ValueError(f"Unmapped controller settings: {sorted(unknown)}")
    actions, offset = array(src, "ACTIONS")
    names = enum_names(src, "Button")
    action_pattern = r'\(("[^"]+"|\w+),\s*(?:Some\(Button::(\w+)\)|None)\)'
    matches = list(re.finditer(action_pattern, actions))
    expected = len(re.findall(r"(?m)^\s*\(", actions))
    if len(matches) != expected:
        raise ValueError("Unparsed controller binding")
    for m in matches:
        action, button = m.groups()
        if action.startswith('"'):
            action = literal(action)
        else:
            action = re.search(fr'const {action}: &str = "([^"]+)"', src)[1]
        results[f"bind_{action}"] = (
            {"type": "enum", "choices": [*names.values(), "none"]},
            names[button] if button else "none",
            f"{PAD}:{where(src, offset + m.start())}", action,
        )

    radial = source(root, RADIAL)
    serialize, offset = section(radial, "pub fn serialize(&self) -> String", "\n}\n")
    layout = re.search(r'pub const LAYOUT_KEY: &str = "([^"]+)"', radial)[1]
    layout_default = int(re.search(r"pub const RADIAL_LAYOUT: u32 = (\d+)", radial)[1])
    results[layout] = (
        {"type": "integer", "meaning": "radial migration version"}, layout_default,
        anchor(RADIAL, radial, r"pub const LAYOUT_KEY"), None,
    )
    radial_types = {"preset": "built-in page identifier", "name": "translation key", "label": "user text",
                    "kind": "page kind", "slots": "comma-separated action or item identifiers"}
    for m in re.finditer(r'&field\(("([^"]+)"|[A-Z_]+)\)', serialize):
        field = m[2]
        if field is None:
            field = re.search(fr'const {m[1]}: &str = "([^"]+)"', radial)[1]
        if field not in radial_types:
            raise ValueError(f"Unmapped radial setting {field}")
        results[f"radial_{{page}}_{field}"] = (
            {"type": radial_types[field]}, None, f"{RADIAL}:{where(radial, offset + m.start())}", None,
        )
    return results


def controller_controls(root):
    src = source(root, CONTROLLER_UI)
    options, offset = section(src, "pub fn rows()", "\n#[")
    matches = list(re.finditer(r"\bPage::(General|Bindings|Device),", options))
    pages = {}
    for i, mark in enumerate(matches):
        end = matches[i + 1].start() if i + 1 < len(matches) else len(options)
        part = options[mark.end():end]
        for m in re.finditer(r'\b(?:option|switch)\(\s*(?:Opt|Switch)::(\w+),\s*"([^"]+)"', part):
            pages[m[1]] = (mark[1], m[2], f"{CONTROLLER_UI}:{where(src, offset + mark.end() + m.start())}")
    leftovers = set(re.findall(r"\b(?:option|switch)\(\s*(?:Opt|Switch)::(\w+)", options)) - pages.keys()
    if leftovers:
        raise ValueError(f"Unparsed controller controls: {sorted(leftovers)}")
    captions = re.findall(r'(?:Opt|Switch)::(\w+)\s*=>\s*"(horizon.options.[^"]+)"',
                          source(root, OPTIONS_UI) + src)
    return pages, dict(captions)


def controller_rows(official, uwp, coverage):
    defs, port_defs = controller_defs(official), controller_defs(uwp)
    pages, captions = controller_controls(official)
    upages, _ = controller_controls(uwp)
    unlinked = pages.keys() - set(PAD_CONTROLS.values())
    if unlinked:
        raise ValueError(f"Unlinked controller controls: {sorted(unlinked)}")
    rows = []
    for key, (values, default, definition, binding) in defs.items():
        control = PAD_CONTROLS.get(key)
        refs = ["official:" + definition]
        label, page, why = None, [], None
        if control in pages:
            tab, why, location = pages[control]
            label, page = captions.get(control), [f"Controller > {tab}"]
            refs.append("official:" + location)
            coverage["official:" + CONTROLLER_UI].append(control)
            if control in upages:
                refs.append("uwp:" + upages[control][2])
                coverage["uwp:" + CONTROLLER_UI].append(control)
        elif binding:
            label = {
                "pad.hotbarPrevious": "Hotbar: Previous Slot",
                "pad.hotbarNext": "Hotbar: Next Slot",
                "pad.radial": "horizon.pad.action.radial",
            }.get(binding, binding)
            page = ["Controller > Bindings"]
            why = f"Use the selected controller button to invoke {binding}."
            for side, root in (("official", official), ("uwp", uwp)):
                refs.append(ref(side, CONTROLLER_UI, source(root, CONTROLLER_UI), r"let binds: Vec<Setting>"))
        elif key.startswith("radial_"):
            field = key.rsplit("_", 1)[1]
            if field in {"label", "slots"}:
                page = ["Controller > Bindings > Radial Menu"]
                label = "horizon.radial.rename" if field == "label" else "horizon.radial.slot.{slot}"
                editor = RENDER + "radial_editor.rs"
                label_ref = r'horizon\.radial\.rename"' if field == "label" else r'horizon\.radial\.slot\.\{slot\}'
                for side, root in (("official", official), ("uwp", uwp)):
                    refs.append(ref(side, editor, source(root, editor), label_ref))
            why = {
                "kind": "Selects the page's generator kind when loading saved settings.",
                "name": "Stores the translation key of a built-in page name.",
                "preset": "Loads the stored built-in radial page preset.",
                "label": "Renames the radial menu page.",
                "slots": "Assigns the action or item shown at each of the eight radial directions.",
            }[field]
        if key in port_defs:
            refs.append("uwp:" + port_defs[key][2])
        record = row(key, "horizon-controller.txt", values, default, refs,
                     localization(official, label), page, scope="setting" if page else "config")
        record["official_ui_label_key"] = label
        if key == "radial_{page}_slots":
            record["official_ui_label"] = ", ".join(
                localization(official, f"horizon.radial.slot.{index}") for index in range(8)
            )
        record["expected_visible_effect"] = why
        record["enabling_conditions"] = ["Controller support and a connected controller"] if page else []
        if key in port_defs:
            record["uwp_persistence"] = "Controller settings parser and save path present in source."
        record["uwp_runtime_action"] = "The UWP controller route is separate from the desktop HID route."
        record["test_method"] = "Set a distinct value using a connected controller, restart the profile, and exercise the affected action."
        if key in ("adaptiveTriggers", "impulseTriggers", "lightbar"):
            path = GAME + "interface/pad/uwp.rs"
            record["sources"].append(ref("uwp", path, source(uwp, path), r"fn pad_output_frame"))
            record["uwp_runtime_action"] = "The UWP controller provider's output callback only expires rumble records. It does not write the HID effect."
            record["root_cause"] = "The desktop controller hardware output extension is excluded by the UWP provider."
            record["uwp_uniform_status"] = "U"
            record["recommendation"] = "Implement the device-specific UWP output and verify it on compatible physical hardware."
        coverage["official:" + definition.rsplit(":", 1)[0]].append(key)
        if key in port_defs:
            coverage["uwp:" + port_defs[key][2].rsplit(":", 1)[0]].append(key)
        rows.append(record)
    for key in sorted(port_defs.keys() - defs.keys()):
        values, default, definition, binding = port_defs[key]
        port_refs = ["uwp:" + definition]
        label = None
        if binding:
            port_refs.append(ref("uwp", CONTROLLER_UI, source(uwp, CONTROLLER_UI), r"let binds: Vec<Setting>"))
            port_refs.append(ref("uwp", PAD, source(uwp, PAD), r'"key.debug.overlay" => Some\(Route'))
            label = "horizon.pad.action.debug_overlay"
            path = "Controller > Bindings"
            runtime = "The port registers a controller mapping for the F3 overlay. Its default binding is unassigned."
            ui = "Listed by the active controller shell from the actions table"
            state = "C"
            cause = None
        elif key == "swapFaceButtons":
            port_refs.append(ref("uwp", OPTIONS_UI, source(uwp, OPTIONS_UI), r"(?m)^ {20}Opt::PadSwapFaceButtons,"))
            shell = RENDER + "options_ui/shell_pages.rs"
            port_refs.append(ref("uwp", shell, source(uwp, shell), r"Page::Controller => Some"))
            pad_route = GAME + "interface/pad/mod.rs"
            port_refs.append(ref("uwp", pad_route, source(uwp, pad_route), r"settings\.swap_face_buttons"))
            label = "horizon.options.swap_face_buttons"
            path = "Controller (legacy fallback)"
            state = "H"
            ui = "Declared in the fallback layout; the active hosted Controller page omits it"
            cause = "The Controller page enters the hosted sheet before the fallback rows could be drawn."
            runtime = "Gamepad input normalization reads the stored flag and swaps face-button routing."
        else:
            raise ValueError(f"Unmapped UWP-only controller key {key}")
        record = row(key, "horizon-controller.txt", values, None, port_refs, scope="port setting")
        record["official_baseline_status"] = "U"
        record["official_windows_behavior"] = "The pinned official controller configuration has no corresponding setting."
        record["uwp_default"] = default
        record["uwp_ui"] = ui
        record["uwp_ui_label"] = localization(uwp, label)
        record["uwp_ui_page"] = path
        record["uwp_persistence"] = "The controller config parser and serializer share this declaration."
        record["uwp_runtime_action"] = runtime
        record["uwp_uniform_status"] = state
        record["root_cause"] = cause
        record["expected_visible_effect"] = runtime
        coverage["uwp:" + definition.rsplit(":", 1)[0]].append(key)
        rows.append(record)
    return rows


CONTEXT = "crates/horizon-mod-package/src/selection/context.rs"
LAUNCH_API = GAME + "runtime/launch_api.rs"
CLI_ALIASES = {
    "--help": ["-h"], "--render-distance": ["--radius"], "--game-dir": ["--gameDir"],
    "--quickPlayMultiplayer": ["--server"],
    "--in-frame": ["--no-server-thread"],
}
CLI_DEFAULTS = {
    "--width": 1280, "--height": 720, "--seed": 0,
    "--game-dir": "starting working directory",
    "--home": "HORIZON_HOME, else the Windows platform data directory",
    "--resources": "resources beside executable if available, else .local/horizon-client/26.3",
}
CLI_ENV = {
    "HORIZON_HOME": "Selects the default Horizon data directory.",
    "HORIZON_PLAYER_NAME": "Supplies a nonempty offline player-name override.",
    "HORIZON_SESSION_FILE": "Supplies a signed-in session JSON path when the launch context allows it.",
    "HORIZON_SPIKES": "Collects slow-frame spans at the supplied millisecond threshold.",
}


def cli_defs(root):
    opts = source(root, LAUNCH)
    context = source(root, CONTEXT)
    launch, launch_offset = section(opts, "pub(super) fn from_launch(", "#[cfg(test)]")
    context, _ = section(context, "pub fn parse(", "#[cfg(test)]")
    pattern = r'(?m)^([ \t]*(?:\|\s*)?(?:"--?[\w-]+"(?:\s*\|\s*"--?[\w-]+")*)(?:\s*=>|\s*\n))'
    flags = {}
    for full, base, file, text in ((launch, launch_offset, LAUNCH, opts),
                                   (context, source(root, CONTEXT).index(context), CONTEXT,
                                    source(root, CONTEXT))):
        for match in re.finditer(pattern, full):
            for key in strings(match[0]):
                flags.setdefault(key, f"{file}:{where(text, base + match.start())}")
    api = source(root, LAUNCH_API)
    options, offset = array(api, "OPTIONS")
    api_defs = {}
    for m in re.finditer(r'option\(\s*"([^"]+)",\s*(None|Some\("([^"]+)"\)),\s*"((?:\\.|[^"])*)",\s*\)', options):
        api_defs[m[1]] = (m[3], literal('"' + m[4] + '"'), f"{LAUNCH_API}:{where(api, offset + m.start())}")
    if set(api_defs) - flags.keys():
        raise ValueError(f"Launch API flags absent from parser: {sorted(set(api_defs) - flags.keys())}")
    return flags, api_defs


def cli_rows(official, uwp, coverage):
    defs, api = cli_defs(official)
    port_defs, _ = cli_defs(uwp)
    aliases = {alias for group in CLI_ALIASES.values() for alias in group}
    rows = []
    for key, definition in defs.items():
        if key in aliases:
            continue
        args, description, api_ref = api.get(key, (None, None, None))
        if args is not None:
            values = {"type": "argument", "metavar": args}
        elif key in api:
            values = {"type": "switch"}
        else:
            values = {"type": "see argument parser"}
        refs = ["official:" + definition]
        keys = [key, *CLI_ALIASES.get(key, [])]
        refs.extend("official:" + defs[alias] for alias in keys[1:])
        if api_ref:
            refs.append("official:" + api_ref)
        refs.extend("uwp:" + port_defs[item] for item in keys if item in port_defs)
        record = row(key, "command line", values, CLI_DEFAULTS.get(key), refs, scope="cli")
        record["aliases"] = keys[1:]
        record["official_windows_behavior"] = description or "Handled by the game launch parser."
        record["expected_visible_effect"] = description
        record["uwp_ui"] = "No in-game control"
        record["uwp_persistence"] = "Process argument"
        record["uwp_runtime_action"] = "Present in the source parser. Packaged activation route unverified." if key in port_defs else "No matching source arm traced."
        record["test_method"] = "Pass the argument via isolated packaged activation and observe its specific effect."
        if key in {"--server-thread", "--in-frame"}:
            official_profile(record, "S", "Deprecated compatibility switch. It leaves server-thread mode unchanged.")
            record["uwp_uniform_status"] = "S"
            record["root_cause"] = "The in-frame single-player path was removed."
            record["recommendation"] = None
        for side, index in (("official", defs), ("uwp", port_defs)):
            for item in keys:
                if item in index:
                    coverage[side + ":" + index[item].rsplit(":", 1)[0]].append(item)
        rows.append(record)
    opts, port = source(official, LAUNCH), source(uwp, LAUNCH)
    origin = {m[1]: f"{LAUNCH}:{where(opts, m.start())}"
              for m in re.finditer(r'std::env::var(?:_os)?\("(HORIZON_\w+)"\)', opts)}
    if set(origin) != CLI_ENV.keys():
        raise ValueError(f"Unmapped launch environment settings: {sorted(set(origin) ^ CLI_ENV.keys())}")
    for key, definition in origin.items():
        refs = ["official:" + definition]
        pattern = fr'std::env::var(?:_os)?\("{key}"\)'
        if re.search(pattern, port):
            refs.append(ref("uwp", LAUNCH, port, pattern))
            coverage["uwp:" + LAUNCH].append(key)
        record = row(key, "environment", {"type": "string"}, None, refs, scope="developer")
        record["official_windows_behavior"] = CLI_ENV[key]
        record["expected_visible_effect"] = CLI_ENV[key]
        record["uwp_persistence"] = "Process environment"
        record["test_method"] = "Launch an isolated process with and without the variable and observe the documented startup effect."
        coverage["official:" + LAUNCH].append(key)
        rows.append(record)
    return rows


def inventory(official: Path, uwp: Path) -> tuple[list[dict], dict[str, list[str]]]:
    """Extract declared keys and linked settings UI locations from both trees."""
    coverage = defaultdict(list)
    records = option_rows(official, uwp, coverage)
    records.extend(controller_rows(official, uwp, coverage))
    records.extend(cli_rows(official, uwp, coverage))
    records.extend(debug_rows(official, uwp, coverage))
    return records, {path: sorted(set(declarations)) for path, declarations in sorted(coverage.items())}



DEBUG_ENTRIES = "crates/horizon-client/src/debug_entries.rs"
DEBUG_UI = GAME + "interface/debug_options.rs"
DEBUG_RENDERERS = GAME + "interface/debug_renderers.rs"


def debug_defs(root):
    src = source(root, DEBUG_ENTRIES)
    entries, offset = array(src, "ENTRIES")
    profiles, _ = section(src, "fn statuses(", "/// \x60DebugEntryCategory")
    defaults = dict(re.findall(r'\("(\w+)", (\w+)\)', section(profiles, "Profile::Default", "Profile::Performance")[0]))
    default_names = enum_names(src, "Status")
    expected = int(re.search(r"const ENTRIES: \[Entry; (\d+)\]", src)[1])
    parsed = {}
    for match in re.finditer(r'(text|renderer)\("(\w+)"(?:, (true|false))?\)', entries):
        definition = f"{DEBUG_ENTRIES}:{where(src, offset + match.start())}"
        parsed[match[2]] = (default_names[defaults.get(match[2], "Never")],
                            match[1], match[3] == "true", definition)
    if len(parsed) != expected:
        raise ValueError(f"Unparsed built-in debug entries: expected {expected}, got {len(parsed)}")
    return parsed, list(default_names.values())


def debug_rows(official, uwp, coverage):
    baseline, values = debug_defs(official)
    port, _ = debug_defs(uwp)
    rows = []
    ui_refs = [ref(side, DEBUG_UI, source(root, DEBUG_UI), r"fn rows\(")
               for side, root in (("official", official), ("uwp", uwp))]
    renderers = source(official, DEBUG_RENDERERS)
    port_renderers = source(uwp, DEBUG_RENDERERS)
    documented = section(renderers, "//! No-ops", "\nuse ")[0]
    port_documented = section(port_renderers, "//! No-ops", "\nuse ")[0]
    placeholder = set(re.findall(r"\x60([a-z_]+)\x60", documented))
    port_placeholder = set(re.findall(r"\x60([a-z_]+)\x60", port_documented))
    for path, (default, category, reduced, definition) in baseline.items():
        key = "minecraft:" + path
        refs = ["official:" + definition, *ui_refs]
        if path in port:
            refs.append("uwp:" + port[path][3])
        item = row(key, "debug-profile.json", {"type": "enum", "choices": values},
                   default, refs, path, [f"F3+F6 Debug Options > {category.title()}"],
                   scope="developer")
        item["aliases"] = [path]
        item["expected_visible_effect"] = f"Show the {path} debug {category} in the selected visibility mode."
        if not reduced:
            item["enabling_conditions"] = ["Full debug information is allowed."]
        if path in port:
            item["uwp_persistence"] = "Presence controls a custom per-entry status in the debug profile."
        if path in placeholder:
            for side, src in (("official", renderers), ("uwp", port_renderers)):
                item["sources"].append(ref(side, DEBUG_RENDERERS, src, f"\x60{path}\x60"))
            official_profile(item, "S", "The official renderer registers the control but documents the debug draw as a no-op.")
            if path in port_placeholder:
                item["uwp_uniform_status"] = "S"
                item["uwp_runtime_action"] = "The saved entry has no implemented debug draw."
                item["root_cause"] = "The relevant vanilla debug data producer is absent."
        coverage["official:" + DEBUG_ENTRIES].append(key)
        coverage["official:" + DEBUG_UI].append(f"DebugOptions/{key}")
        if path in port:
            coverage["uwp:" + DEBUG_ENTRIES].append(key)
            coverage["uwp:" + DEBUG_UI].append(f"DebugOptions/{key}")
        rows.append(item)
    for key, values, default, label, pattern in (
        ("profile", {"type": "enum", "choices": ["default", "performance"]}, "default",
         "debug.options.profile.default / debug.options.profile.performance", r"pub enum Profile"),
        ("DataVersion", {"type": "integer"}, 5023, None, r'"DataVersion".into\(\)'),
    ):
        refs = [ref(side, DEBUG_ENTRIES, source(root, DEBUG_ENTRIES), pattern)
                for side, root in (("official", official), ("uwp", uwp))]
        item = row(key, "debug-profile.json", values, default, refs, label,
                   ["F3+F6 Debug Options"] if label else [], scope="developer")
        item["uwp_persistence"] = "Debug profile serializer includes this property."
        item["official_windows_behavior"] = "The selected preset replaces custom debug-entry statuses." if label else "The writer emits the pinned game data version."
        for side in ("official", "uwp"):
            coverage[side + ":" + DEBUG_ENTRIES].append(key)
        rows.append(item)
    return rows

OPTIONS_RUNTIME = {
    "allowCursorChanges": (GAME + "interface/pointer_capture.rs", r"if self\.settings\.allow_cursor_changes",
                           "Allow hover and editing controls to change the native pointer icon when keyboard focus is not driving the UI."),
    "allowServerListing": (SETTINGS, r'allows_listing: self\.file\.bool\("allowServerListing"',
                           "Send the player's listing preference in Client Information. Server handling determines whether a server list shows the player."),
    "autoJump": ("crates/horizon-client/src/game.rs", r"player\.auto_jump_enabled = self\.settings\.auto_jump",
                 "Allow the movement simulation to jump over eligible obstacles while moving forward."),
    "autoSuggestions": (GAME + "interface/chat.rs", r"screen\.auto_suggestions = self\.settings\.auto_suggestions",
                        "Automatically open command suggestions as the player types."),
    "backgroundForChatOnly": (GAME + "interface/chat.rs", r'self\.settings\.file\.bool\("backgroundForChatOnly"',
                              "Confine the configured text backdrop to chat messages instead of applying it to the chat input and other applicable text."),
    "bobView": (GAME + "interface/fov.rs", r"bob_matrix\(&state, tilt, self\.settings\.view_bobbing",
                "Enable camera bobbing from player movement."),
    "chatColors": (SETTINGS, r"colors: self\.chat_colors,",
                   "Choose whether chat messages display their text colors."),
    "chatDelay": (GAME + "network/remote/chat_queue.rs", r"self\.settings\.chat_delay \* 1000",
                  "Space out received chat messages by the configured interval in seconds."),
    "chatHeightFocused": (SETTINGS, r"height_focused: f64::from\(self\.chat_height_focused",
                          "Change the number of chat lines that fit when chat is open."),
    "chatHeightUnfocused": (SETTINGS, r"height_unfocused: f64::from\(self\.chat_height_unfocused",
                            "Change the number of lines that fit in the default gameplay chat overlay."),
    "chatLineSpacing": (SETTINGS, r"line_spacing: f64::from\(self\.chat_line_spacing",
                        "Adjust vertical spacing between lines in the chat overlay."),
    "chatLinks": (GAME + "interface/chat.rs", r"if !self\.settings\.chat_links",
                  "Enable URL activation from a chat message."),
    "chatLinksPrompt": (GAME + "interface/chat.rs", r"if self\.settings\.chat_links_prompt",
                        "Show a confirmation before opening a permitted chat URL."),
    "chatOpacity": (SETTINGS, r"opacity: f64::from\(self\.chat_opacity",
                    "Change foreground opacity for messages in the chat overlay."),
    "chatScale": (SETTINGS, r"scale: f64::from\(self\.chat_scale",
                  "Scale chat text and its layout."),
    "chatVisibility": (GAME + "interface/chat.rs", r"ChatVisibility::from_option\(self\.settings\.chat_visibility",
                       "Select full chat, commands only, or hidden chat. The value also shapes local chat permissions."),
    "chatWidth": (SETTINGS, r"width: f64::from\(self\.chat_width",
                  "Change chat overlay width and where its messages wrap."),
    "damageTiltStrength": (GAME + "interface/fov.rs", r'self\.settings\.file\.double\("damageTiltStrength"',
                           "Scale the camera's tilt when the player takes damage."),
    "darkMojangStudiosBackground": (GAME + "interface/startup_menu.rs", r'self\.settings\.file\.bool\("darkMojangStudiosBackground"',
                                   "Use the alternate dark background on the startup or reload logo screen."),
    "darknessEffectScale": (GAME + "blocks/sculk.rs", r'self\.settings\.file\.double\("darknessEffectScale"',
                            "Scale the visual pulsing from the Darkness effect."),
    "debugGuiScale": (DEBUG_UI,
                      r'fn debug_gui_scale_label',
                      "Set the debug overlay scale independently. The Unchanged choice shares the normal GUI scale."),
    "discrete_mouse_scroll": ("crates/horizon-client/src/game.rs", r"let discrete = self\.settings\.discrete_scroll",
                              "Treat wheel motion as discrete steps instead of using the fractional movement of the wheel."),
    "glintSpeed": (GAME + "interface/item_draw.rs", r'speed: self\.settings\.file\.double\("glintSpeed"',
                   "Change the animation speed of the enchantment glint."),
    "glintStrength": (GAME + "interface/item_draw.rs", r'alpha: self\.settings\.file\.double\("glintStrength"',
                      "Change the opacity of the enchantment glint."),
    "hideLightningFlashes": (GAME + "world/end.rs", r'!self\.settings\.file\.bool\("hideLightningFlashes"',
                             "Suppress the lightning-related flash rendered by the world."),
    "hideServerAddress": (GAME + "interface/startup_menu.rs", r'self\.settings\.file\.bool\("hideServerAddress"',
                          "Hide server addresses from the multiplayer list presentation."),
    "hideSplashTexts": (GAME + "interface/startup_menu.rs", r'self\.settings\.file\.bool\("hideSplashTexts"',
                        "Hide the rotating splash text on the title screen."),
    "highContrast": (OPTIONS_UI, r'let on = s\.resource_packs\.iter\(\)\.any\(\|id\| id == "high_contrast"',
                     "Toggle the built-in high-contrast resource pack for menus and other GUI elements."),
    "highContrastBlockOutline": ("crates/horizon-client/src/game.rs", r'outline_high_contrast: self\.settings\.file\.bool\("highContrastBlockOutline"',
                                 "Draw the focused block outline with the high-contrast outline style."),
    "invertXMouse": (INPUT + "mouse.rs", r"if look\.invert_x \{ -xo \}",
                     "Reverse the horizontal mouse-look axis."),
    "invertYMouse": (INPUT + "mouse.rs", r"if look\.invert_y \{ -yo \}",
                     "Reverse the vertical mouse-look axis."),
    "joinedFirstServer": (GAME + "interface/tutorial.rs", r'if !self\.settings\.file\.bool\("joinedFirstServer"',
                          "Show the Social Interactions hint once on a server or after publishing a world to LAN."),
    "lastServer": (GAME + "interface/startup_menu.rs", r'title\.last_server = settings\.file\.string\("lastServer"',
                   "Prepopulate the Direct Connection address field with the saved address."),
    "mainHand": (GAME + "interface/frame_geometry.rs", r"let left = self\.settings\.main_hand_left",
                 "Choose left or right for the primary hand and its first-person held item."),
    "mouseSensitivity": (GAME + "interface/pointer_capture.rs", r"sensitivity: (?:f64::from\(self\.settings\.sensitivity|self\.settings\.sensitivity)",
                         "Scale how far the view turns per unit of mouse motion."),
    "mouseWheelSensitivity": ("crates/horizon-client/src/game.rs", r"self\.settings\.mouse_wheel_sensitivity",
                              "Scale wheel motion before dispatching scroll and hotbar actions."),
    "notificationDisplayTime": ("crates/horizon-client/src/game.rs", r"title\.update_toast\(self\.settings\.notification_display_time",
                                "Scale the time that toasts and other duration-managed notifications stay visible."),
    "onboardAccessibility": (GAME + "interface/startup_menu.rs", r'if settings\.file\.bool\("onboardAccessibility"',
                             "Open accessibility onboarding at startup until the flow records completion."),
    "onlyShowSecureChat": (GAME + "network/remote/mod.rs", r'if matches!\(trust, ChatTrust::NotSecure\) && self\.settings\.file\.bool\("onlyShowSecureChat"',
                           "Drop remote player chat classified as insecure and use the original signed body in place of unsigned decoration."),
    "operatorItemsTab": (GAME + "interface/creative_screen.rs", r"self\.can_use_game_master_blocks\(\) && self\.settings\.operator_items_tab",
                         "Show the operator items tab in Creative when the local player has the required game-master permission."),
    "panoramaScrollSpeed": (GAME + "runtime/loading_screen.rs", r'self\.settings\.file\.double\("panoramaScrollSpeed"',
                            "Scale the rotation speed of the title-screen panorama."),
    "preferredGraphicsBackend": (SETTINGS, r'api: match f\.name\("preferredGraphicsBackend"',
                                 "Load a legacy graphics API preference as Auto, OpenGL, or Vulkan. A separately saved Horizon Graphics API can take precedence."),
    "quitShortcuts": (GAME + "interface/startup_menu.rs", r"if !self\.settings\.quit_shortcuts",
                      "Allow Alt+F4 to close the Windows client via the platform close request when enabled."),
    "reducedDebugInfo": (GAME + "interface/debug_keys.rs", r"self\.settings\.reduced_debug_info",
                         "Restrict debug overlay data and debug actions that require full debug information."),
    "rotateWithMinecart": (GAME + "network/remote_vehicles.rs", r'self\.minecart_improvements\(\) && self\.settings\.file\.bool\("rotateWithMinecart"',
                           "Turn the player's view with the ridden minecart when minecart improvements are enabled."),
    "saveChatDrafts": (GAME + "interface/chat.rs", r'should_discard_draft\(reason, self\.settings\.file\.bool\("saveChatDrafts"',
                       "Retain the unsent chat input when the relevant close reason permits keeping a draft."),
    "screenEffectScale": (GAME + "interface/fov.rs", r'fn screen_effect_scale',
                          "Scale screen distortion, including the nausea field-of-view warp."),
    "skipMultiplayerWarning": ("crates/horizon-client/src/title.rs", r"self\.set_mode\(if self\.skip_multiplayer_warning",
                               "Skip the multiplayer warning when opening the multiplayer screen."),
    "sprintWindow": (GAME + "network/remote/mod.rs", r"let sprint_window = self\.settings\.sprint_window",
                     "Set the tick window for the forward double-tap sprint gesture."),
    "startedCleanly": (GAME + "runtime/loading_screen.rs", r"pub\(super\) fn unclean_start_recovery",
                       "Guide next-launch recovery. A saved false clears the fullscreen request and its stored resolution."),
    "textBackgroundOpacity": (SETTINGS, r"background_opacity: f64::from\(self\.text_background_opacity",
                              "Scale opacity of the background behind chat and other text using the shared text-background setting."),
    "toggleAttack": (GAME + "interface/play_input.rs", r"self\.settings\.toggle_attack,",
                     "Latch the attack action between presses instead of requiring it to be held."),
    "toggleCrouch": (GAME + "interface/play_input.rs", r"self\.settings\.toggle_crouch,",
                     "Latch crouching between presses instead of requiring the key to be held."),
    "toggleSprint": (GAME + "interface/play_input.rs", r"self\.settings\.toggle_sprint,",
                     "Latch the sprint input between presses instead of requiring it to be held."),
    "toggleUse": (GAME + "interface/play_input.rs", r"self\.settings\.toggle_use,",
                  "Latch the use action between presses instead of requiring it to be held."),
    "tutorialStep": (GAME + "interface/tutorial.rs", r'Step::from_option\(self\.settings\.file\.int\("tutorialStep"',
                     "Resume the saved tutorial checkpoint and determine which tutorial hint can appear next."),
    "version": (RENDER + "options_fixes.rs", r"pub fn update\(raw: &mut Raw, version: i32",
                "Select the options-file migration steps. There is no standalone user-visible control."),
}

OPTIONS_WITHOUT_CONSUMER = {
    "glDebugVerbosity": "The pinned options file retains this OpenGL debug setting. A logging change could not be traced beyond storage.",
    "macFullscreenMenuVisibility": "The pinned file retains this macOS fullscreen preference. There is no Windows control or effect traced.",
    "overrideHeight": "The pinned file retains this window-height setting. The traced window-sizing path uses launch dimensions.",
    "overrideWidth": "The pinned file retains this window-width setting. The traced window-sizing path uses launch dimensions.",
    "syncChunkWrites": "The pinned file retains this client key. No client-side chunk write-policy change was traced for it.",
    "telemetryOptInExtra": "The pinned file retains this consent key. It was not traced to Horizon's telemetry consent or event policy.",
    "useNativeTransport": "The pinned file retains this transport preference. No network transport selection was traced for it.",
}

OPTIONS_UNAVAILABLE_EFFECTS = {
    "ctrlClickEmulatesRightClick": "Offer a Control-click right-click emulation switch on macOS. The Windows controls list excludes it.",
    "hideMatchedNames": "The intended control would hide chat matching blocked player names. The pinned control is disabled.",
    "inGameNotification": "The intended control would toggle in-game friend notifications. The pinned control is disabled.",
    "realmsNotifications": "The intended control would toggle Realms notifications. The pinned control is disabled.",
    "sharePresence": "The intended control would select whether to share friend presence. The pinned control is disabled.",
}
