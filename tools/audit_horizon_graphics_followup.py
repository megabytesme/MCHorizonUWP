"""Bounded, source-backed graphics follow-up for existing audit rows.

The caller supplies the patched pinned UWP source tree. This module creates no
inventory rows and never turns persistence or a world smoke test into a pass.
"""
from pathlib import Path
from audit_horizon_evidence import ACTIVE, DX11

GAME = 'crates/horizon-client/src/game.rs'
GFX = 'crates/horizon-client-render/src/gfx.rs'
UI = 'crates/horizon-client-render/src/graphics_ui.rs'
POST = 'crates/horizon-client-render/src/gfx/pack_post.rs'
LOAD = 'crates/horizon-client/src/game/runtime/resource_load.rs'
PRESET = 'crates/horizon-client-render/src/graphics_preset.rs'
WORLD = 'crates/horizon-client/src/game/world/world_loading.rs'

# Paths and unique source fragments are checked against the supplied patched pin.
RULES = {
 'graphics.shadowCache': ([(GAME, 'shadow_cache: self.settings.horizon.shadow_cache'), (POST, 'fn shadow_cache_hit')], 'The frame forwards this value to classic pack shadow-cache logic; a matching eligible cache key and shadow targets are required.', 'With a compatible classic shadow pack on a supported backend compare Off/On in a static scene; count shadow draws and pixels, then move the sun or change the scene and confirm a fresh shadow. Restart and repeat.'),
 'graphics.overlay': ([(GAME, '.draw(self.settings.horizon.overlay,')], 'The overlay selector is passed into the HUD overlay draw.', 'At a fixed world and window cycle Off and available overlay modes; compare graph/text pixels and frame timing data, then restart and confirm selection.'),
 'graphics.textureBudgetMiB': ([(LOAD, 'set_texture_budget(settings.horizon.texture_budget_mib)')], 'Resource loading passes the stored MiB limit to the sprite texture budget; this is not evidence of a changed resident texture.', 'Set 0 and a small valid nonzero value on a texture-heavy resource pack; reload resources and compare chosen top mip/texture allocations and image, then restart. Record device limits and actual allocation.'),
 'graphics.sky': ([(GAME, 'skybox: self.overworld_skybox() && self.settings.horizon.sky'), (GAME, 'stars: stars.filter(|_| self.settings.horizon.sky || title)')], 'World skybox and stars depend on the setting; title rendering is an explicit exception.', 'Use the same overworld view at night with clear weather; compare On/Off sky, stars and celestial bodies, and reopen. Test End and title separately.'),
 'graphics.fog': ([('crates/horizon-client/src/game/interface/render_tick.rs', 'if !self.settings.horizon.fog {')], 'The frame fog calculation branches on the setting; fog output also depends on world and view conditions.', 'In a distant terrain scene compare On/Off at fixed weather and camera; inspect the fog parameters and far-edge pixels, then reopen.'),
 'graphics.lodDistance': ([('crates/horizon-client/src/game/interface/render_settings.rs', 'fn far_terrain_choice(&self)')], 'Far-terrain choice is computed by the game and is subject to effective runtime policy and available terrain.', 'In a prepared world compare 0 and a nonzero range, log effective distance and far-terrain draw/geometry, capture matching views and reopen.'),
 'graphics.weather': ([(GAME, 'self.can_have_weather() && self.settings.horizon.weather')], 'World weather is constructed only outside title when the dimension permits it and the setting is enabled.', 'In a rain-capable dimension force rain, compare On/Off from a fixed camera and reopen; separately test snow and a dimension without weather.'),
 'graphics.animatedTextures': ([(GFX, 'if self.animated_textures {')], 'The renderer guards animated texture work with its current animated-textures state.', 'At fixed position capture multiple ticks of water, lava and fire with On and Off; verify texture update activity and resume after restart.'),
 'graphics.preset': ([(UI, 'let preset = step(&Preset::ALL'), (PRESET, 'g.preset = preset;')], 'The graphics UI steps the preset and applies the corresponding shared settings; downstream backend gates still apply independently.', 'Record every constituent value before and after Performance and Ultra, change one constituent to obtain Custom, restart, then independently measure each affected backend effect.'),
 'options.txt:guiScale': ([(GAME, 'HudBatch::new(size, self.settings.gui_scale_inputs())')], 'HUD batching consumes GUI scale inputs at the current output size.', 'Compare Auto, 1 and a larger supported GUI scale at fixed output and world scale; measure HUD bounds and hit targets, restart and repeat.'),
 'options.txt:renderDistance': ([(WORLD, 'streaming.radius = settings.render_distance;')], 'World loading initializes streaming radius from render distance; server and world conditions can bound visible terrain.', 'In the same locally controlled world compare 4 and 12 chunks, record actual streaming radius and loaded section extents after settling, restart and repeat; record any server limit.'),
}

def _refs(root, fragments):
    refs = []
    for path, needle in fragments:
        file = Path(root) / path
        if not file.is_file():
            return []
        lines = file.read_text(encoding='utf-8').splitlines()
        line = next((i for i, text in enumerate(lines, 1) if needle in text), None)
        if line is None:
            return []
        refs.append(f'uwp:{path}:{line}')
    return refs


def apply_graphics(settings, features, product, uwp):
    """Add bounded source trace and test acceptance, preserving all row IDs and V cells."""
    del features, product  # No independent feature-level evidence is asserted.
    by_id = {row.get('id'): row for row in settings}
    for identifier, (fragments, route, test) in RULES.items():
        row = by_id.get(identifier)
        refs = _refs(uwp, fragments)
        if row is None or not refs:
            continue
        sources = row.setdefault('sources', [])
        sources.extend(ref for ref in refs if ref not in sources)
        row['uwp_runtime_action'] = route + ' This is source evidence, not a signed A/B result.'
        row['test_method'] = test
        row['recommendation'] = 'Run the focused A/B on each signed profile and retain settings, frames and telemetry.'
        for profile in ACTIVE:
            cells = row.setdefault('profiles', {})
            old = cells.get(profile)
            if old and old.get('status') == 'V':
                continue
            condition = route + ' ' + test
            if identifier == 'graphics.shadowCache' and profile in DX11:
                condition = ('The baseline D3D11 compatibility path refuses shader packs; '
                             'the pack cache cannot be exercised there. ' + test)
                status = 'H'
            else:
                status = 'C'
            if old and old.get('status') not in ('?', 'C'):
                old['condition'] = old.get('condition', '') + ' Follow-up: ' + condition
            else:
                cells[profile] = {'status': status, 'condition': condition, 'evidence': (old or {}).get('evidence', [])}
