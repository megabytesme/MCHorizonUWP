# HorizonUWP feature parity

Official reference is the **20 November 2026 Nightly** at `0a875da91dbb0fdcb7ba8c42c11341aa229af51a`.
The immutable tag is `nightly-source/2026-11-20-0a875da`. The 59,856,486-byte release source archive matches SHA-256 `921fc63efde1d8c226ba6c0457677b41effd7be367ba93116ccb424a9790eb75` in the official checksums and release asset metadata. The full Git range from the 18 November source contains 331 commits and 1,327 changed paths with rename detection disabled.
[Official Nightly](https://github.com/horizon-rust/horizon/releases/tag/nightly) · [Pinned revision](https://github.com/horizon-rust/horizon/commit/0a875da91dbb0fdcb7ba8c42c11341aa229af51a)

The audited UWP baseline is `2e98d4555313e2d9485c207ddc1962e2636001bd` (Builder 1.0.6, integration patch 0.1.12, client archive 0.1.13), built from `0b14f8acfd97fa2c750e42795249dcc981178ca4`. The runtime matrix records the verified November 18 port baseline. November 20 source evidence establishes inventory and code paths. Verify the migrated signed packages independently before transferring runtime statuses.
The [JSON matrix](../deliverables/nightly-2026-11-20/feature-parity.json) holds 108 feature groups across 11 architecture/backend profiles. It indexes all 54 pinned `Graphics` fields and 31 pinned `PadSettings` fields, 699 typed hook declarations and 178 modding-book source pages. Runtime evidence covers individual routes and test fixtures. The hook inventory records declarations, with only demonstrated hooks carrying evidence.

The official SDK metadata identifies Rust 1.99.1, package format 6, content API 0.2 and the additive source surface `e05637790d2a91aaa8075e748e2ca872`. These are reference values for the official release. The Builder must fingerprint the patched UWP native build and verify the Wasm standard library and guest binding against the pinned engine. [SDK format](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/reference/sdk.md)

## November 18 runtime and source summary

- **Working in signed builds.** The same x64 multiplayer scene rendered through D3D11, D3D12 and hardware ANGLE. Signed x64 and x86 D3D11 builds drew and toggled both native and Wasm GUI fixtures. Surface D3D11 rendered a saved world after the HAL depth-state correction, and Surface DX12 rendered the title and both mod hooks. Lumia 1.0.0.8 opened an existing world, drew readable UI and both mod hooks, and had audible music, usable touch and GameSir handoff during short play. [Desktop graphics](nightly-desktop-graphics-2026-11-20.md) · [Mod runtime](nightly-mod-runtime-2026-11-20.md) · [ARM replay](nightly-arm-hud-2026-11-20.md) · [Mobile sessions](nightly-mobile-2026-11-20.md)
- **Visible gaps backed by code.** The UWP bridge turns off upstream client defaults and omits the Play with Friends tunnel and desktop HID output extensions. The UWP narrator is inactive. UWP XAudio2 opens default output and the non-CPAL settings implementation cannot enumerate or switch devices. The desktop launcher's provisioning, versions, CLI and modpack workflow are split between Builder and the XAML shell, with partial coverage. [Bridge features](../uwp/game-bridge/Cargo.toml) · [Pre-game shell](../uwp/pregame/README.md) · [Baseline patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)
- **FL9_1 compatibility affects all packaged D3D11 profiles.** The baseline `Fl91Caps::from_adapter` checks the backend rather than the physical feature level. Its restricted world-post and atlas paths disable actual fixed world scaling, adaptive scaling, FXAA/TAA, HDR, shader packs, menu blur, spectator post and FSR frame generation in those packaging profiles. The official November 20 source adds 4× terrain MSAA and TAA upscaling under suitable packs. Those features have backend and format gates and require dedicated validation on modern UWP paths. [Baseline patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [Pinned graphics](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md)
- **Mods have different installation constraints.** Pulley runs interpreted Wasm with the matching shared standard library in AppContainer. Native Rust needs the exact patched engine identity. x64, x86 and ARM64 can use signed executable optional packages with related-set metadata. The verified ARM32 route embeds trusted source at Builder time, and requires a rebuild for code changes. The previous Lumia process hit a registration seal on re-entering the game, then succeeded after full restart. [Native packaging](native-mods-uwp-2026-11-19.md) · [Mod runtime](nightly-mod-runtime-2026-11-20.md)
- **Hardware limits with observed evidence.** The Lumia's 32-chunk test lost the D3D11 device after allocation failures. A backed-up test profile ran in the same saved world at two chunks with a 16 MiB texture budget. The GPU overlay said `n/a`. The reported 64 ms wall and 43 ms CPU measurements cannot be subtracted to infer GPU time. The prior test stayed near 2561×1439 with the configured scale at 50%. [Lumia follow-up](nightly-mobile-2026-11-20.md)

## November 20 UWP candidate

Builder 1.0.7, patch 0.1.13 and client 0.1.14 use the pinned November 20 source. The baseline cells below describe earlier signed packages. The JSON gives candidate statuses for engine routes with new evidence, including fixed scale, adaptive scaling, gyro aiming, world entry and Wasm.

- **Fixed world scaling** uses a separate color and depth target on the compatibility renderer and draws the HUD at output resolution. Win32 x64 and x86 each passed eight readback cases at hardware FL9_1 and eight with the compatibility projection on FL11_1. The signed Surface Pro X AppContainer probe passed eight at physical FL9_1. The signed ARM32 AppContainer probe passed eight offscreen cases on the Lumia’s physical Adreno 430 FL9_1. The signed x64 game rendered a saved world at 770 × 510. The signed x86 game used 1154 × 765. Both kept the HUD at 1539 × 1020. Full-game Lumia scene measurements, touch and its swapchain checks remain open. [DRS report](nightly-drs-2026-11-20.md)
- **Adaptive scaling** applies the configured floor and target FPS with a conservative frame-pressure policy when the adapter cannot supply timestamps. Ten controller tests passed. The full candidate still needs matched sustained runs showing scale changes, the timing behind each change and the resulting frame pacing. [Controller evidence](../deliverables/nightly-2026-11-20/drs/evidence/controller-tests.log)
- **Gyroscope aiming** is now a native client mod using the standard frame hook and local rotation API. The sensor provider stays inside the UWP host. The Surface reported a gyrometer with a 2 ms minimum report interval. Ben-Desktop reported no gyrometer. In-world motion, orientation and input coexistence still need physical verification. [Mod instructions](../uwp/mods/horizon-gyro-aim/README.md)
- **Signed desktop gameplay** reached the same isolated saved world with both native and Wasm markers on x64 D3D11, D3D12 and ANGLE, and x86 D3D11. The exact November 20 stock desktop host also passed the Wasm ABI fixture and isolated the deliberate trap. The later signed x64 runtime verified Wasm trap and deadline isolation, and the Surface rendered both markers on D3D11 and hardware D3D12. [Game and package evidence](nightly-cross-architecture-2026-11-20.md) · [Stock ABI evidence](nightly-2026-11-20-wasm.md)
- **Lumia sensor probe**. The signed Lumia sensor probe reported a 4 ms minimum gyrometer interval and received both an event and a polled sample. [Sensor evidence](../deliverables/nightly-2026-11-20/gyro/lumia-probe/gyro-probe.txt)

## November 21 runtime follow-up

- **Desktop startup, multiplayer and mods.** The updated XAML host removed a synchronous Start-tile broker call from its game dispatcher. Signed x64 and x86 D3D11 clients joined the pinned dedicated server, moved, and reconnected with their saved positions. A signed x64 world verified five independent native/Wasm enable states, rejected an altered native digest, and enforced the Wasm group memory and epoch limits. [Runtime acceptance](nightly-runtime-completion-2026-11-21.md)
- **Surface gameplay.** Signed D3D11 and hardware D3D12 ran the saved world, both hooks, movement, audio and inventory/chat. The D3D11 world was scaled to 67% with the HUD at output size, including after resizing. The D3D12 adaptive observation exposed invalid GPU timestamp data. Source validation and measured fallback are in the newer patch. [Surface and timing evidence](nightly-runtime-completion-2026-11-21.md#surface-gameplay)
- **Lumia saved world.** The signed normal-projection diagnostic reached the two-chunk save with a 4 MiB block texture budget. At 50%, its render target was 1281 × 720 and its output was 2561 × 1439. This single trial used a recovered frame trace. The 75% launch was suspended before world entry. [Lumia first-world log](../deliverables/nightly-2026-11-20/runtime-evidence/arm32/fixed-50-first-world/completed-game.log)
- **Lumia adaptive scaling.** Signed client 0.1.16 selected 55% using frame pressure with a 50% floor and a 30 FPS target. The world target reached 1409 × 791 while the HUD remained at the 2561 × 1439 output. The interactive run included an orientation change and movement, so its trace stands separately from the matched fixed-scale runs. [Lumia measurements](nightly-lumia-drs-2026-11-21.md) · [Phone capture](../deliverables/nightly-2026-11-20/drs/lumia-runtime/v16-adaptive-world-screen.png)
- **Gyro.** The updated mod and provider are in the candidate. The signed Lumia diagnostic rendered the saved world and the physical movement check verified both yaw and pitch. A separate UWP-only writer restores its opt-in numerical trace. Alternate landscape, disable/re-enable with physical motion and controller coexistence still need their own runs. [Gyro settings and proof procedure](../uwp/mods/horizon-gyro-aim/README.md)

## Highest-priority work

| Priority | Local work item | Acceptance evidence |
| --- | --- | --- |
| P0 | Nightly build and runtime regression | Strict-link and sign all four architectures. Prove in-world native, Wasm and content fixtures, safe restart and desktop multiplayer. |
| P0 | FL9_1 world-scale and adaptive tests | Measure actual scaled color/depth and native presentation sizes at 100%, 75%, ~67% and 50%. Preserve native HUD/input mapping and record timing source and scale changes under load. |
| P0 | Gyro aiming and native input coexistence | Verify yaw, pitch, both landscape orientations, touch/gamepad handoff, disabled state and no-sensor fallback on isolated packages. |
| P0 | Mobile memory and lifecycle | Measure sustained frame intervals and memory in the same two-chunk world. Diagnose allocation pressure at higher distances, then check resize, rotation and resume. |
| P1 | Authenticated and accessible gameplay | Verify sign-in and secure refresh, online-mode join and Realms, live chat keyboard/IME and narration on a compatible WinRT implementation. |
| P1 | Graphics capability explanations | Add precise disabled reasons for the real backend. Independently verify F2 and the new mod screenshot and quit hooks. |
| P2 | Advanced backend features | Measure 4× MSAA, TAA, shader-pack stages and HDR on available DX12 hardware. Verify independent ANGLE and D3D11 limits. |

## Matrix legend and profiles

Each cell describes the tested scope for the listed feature and backend. A verified gameplay route does not establish every mode, option or hook within a feature group. Optional renderer cells rely on their own execution evidence, and unknown is used when a route or adapter has not been established.

| Status | Meaning |
| --- | --- |
| V | Fully supported and verified |
| L | Supported with limitations |
| I | Implemented but not physically tested |
| U | Unsupported by the current UWP port |
| G | Unsupported by the graphics backend or feature level |
| H | Unsupported by the operating system/hardware |
| D | Intentionally disabled |
| X | Unknown — further investigation required |

| Column | Configuration |
| --- | --- |
| x64 11 | x64 D3D11 compatibility path (bench tested on physical FL11_1) |
| x64 12 | x64 D3D12 hardware |
| x64 GL | x64 ANGLE OpenGL ES 3.0 |
| x86 11 | x86 D3D11 compatibility path |
| x86 12 | x86 D3D12 build route, runtime unverified |
| x86 GL | x86 ANGLE, runtime availability unverified |
| A64 11 | ARM64 D3D11 compatibility path, Surface Adreno 680 |
| A64 12 | ARM64 D3D12, Surface Adreno 680 |
| A32 11 | ARM32 D3D11 FL9_1, Lumia Adreno 430 |
| A32 12 | ARM32 D3D12 alternatives. Earlier device probe rejected available adapter |
| A32 GL | ARM32 ANGLE alternatives. Earlier device EGL surface probe failed |

## Core gameplay and networking

| ID | Feature | x64 11 | x64 12 | x64 GL | x86 11 | x86 12 | x86 GL | A64 11 | A64 12 | A32 11 | A32 12 | A32 GL |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| world-entry | Open and play a saved single-player world | I | I | I | I | I | X | L | I | L | X | X |
| world-create | Create a new local world with seed, presets and data packs | I | I | I | I | I | X | I | I | L | X | X |
| survival | Survival, health, food, damage, mobs and progression | I | I | I | I | I | X | I | I | I | X | X |
| creative | Creative mode, inventory, flight and building | L | L | L | I | L | X | I | L | I | X | X |
| other-modes | Adventure, spectator and hardcore modes | I | I | I | I | I | X | I | I | I | X | X |
| gameplay-systems | Crafting, smelting, enchanting, trading, combat, redstone, farming, dimensions and boss/raid progression | I | I | I | I | I | X | I | I | I | X | X |
| chunks | Chunk generation, loading, meshing, uploads and persistence | V | V | V | I | V | X | L | V | L | X | X |
| saves | Java-format world data, operational saves, upgrades and restart | I | I | I | I | I | X | L | I | L | X | X |
| commands | Vanilla commands, suggestions and operator actions | I | I | I | I | I | X | I | I | I | X | X |
| multiplayer | Direct Connect and Java protocol multiplayer | V | V | V | I | V | X | I | I | I | X | X |
| server-discovery | Saved servers, discovery, status, LAN open/join and network advertisements | L | L | L | I | L | X | X | I | L | X | X |
| play-with-friends | Play with Friends invite-code tunnel and optional relay | U | U | U | U | U | U | U | U | U | U | U |
| online-account | Microsoft account sign-in, Minecraft profile/ownership, session refresh, online-mode join | I | I | I | I | I | X | I | I | I | X | X |
| realms | Realms service, availability, notifications, lists and join | X | X | X | X | X | X | X | X | X | X | X |
| cross-version | Version selection and Minecraft protocol compatibility | L | L | L | L | L | X | L | L | L | X | X |
| dedicated | Standalone horizon-server, configuration and host management | U | U | U | U | U | U | U | U | U | U | U |
| packs | Vanilla resource packs, data packs and server-required resource packs | I | I | I | I | I | X | I | I | I | X | X |
| optifine-resources | OptiFine and Polytone resource customizations documented upstream | I | I | I | I | I | X | I | I | I | X | X |
| skins | Player skins, cosmetics and optional presentation features | I | I | I | I | I | X | I | I | I | X | X |
| extras | Fullbright, dynamic lights, mouse tweaks, sorting, chat animation, zoom detail and other stock extras | I | I | I | I | I | X | I | I | I | X | X |
| split-screen | Upstream splitscreen extra and multi-input seat routing | X | X | X | X | X | X | X | X | X | X | X |
| starling-touch | Horizon touch paths where supplied by target platform | I | I | I | I | I | X | I | I | V | X | X |

**world-entry**. Integrated server and rendering were demonstrated in saved worlds on Surface D3D11 and Lumia. The Lumia passed a short run at two chunks. Existing x64 graphics evidence is multiplayer. It does not establish this exact signed build's single-player test.

**P0**. Retest single player and clean exit on all four native targets. Standard mod route **no**. UWP host bridge **no**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [world-data.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/world-data.md) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md)

**world-create**. World creation UI and server machinery ship. Prior Lumia world creation was initially blocked by a renderer panic. The fixed run opened a saved world, leaving new-world creation and data pack options to test.

**P0**. Create a disposable world per architecture, save, quit and reopen. Standard mod route **no**. UWP host bridge **no**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [world-data.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/world-data.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**survival**. Uses the shared game and integrated server. The evidence records world entry and input, without a matched survival progression or damage test.

**P1**. Run a representative survival loop on desktop and mobile. Standard mod route **no**. UWP host bridge **no**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**creative**. The three desktop renderers joined a mode-creative scene. The scene does not cover every creative inventory operation. Record the per-mode gameplay test separately.

**P1**. Verify create, place, break, fly and inventory in isolated worlds. Standard mod route **no**. UWP host bridge **no**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md)

**other-modes**. Shared world and rules code is carried. The baseline reports do not cover the three modes.

**P2**. Exercise each mode and reopen the resulting saves. Standard mod route **no**. UWP host bridge **no**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**gameplay-systems**. Shared engine behavior is inherited. None of the short hardware sessions is a cross-feature progression suite.

**P2**. Run vanilla-parity harnesses and a small end-to-end gameplay suite against signed candidates. Standard mod route **no**. UWP host bridge **no**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [world-data.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/world-data.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**chunks**. ARM32 runs a compatibility path with constrained texture budget and upload batching. A 32-chunk run hit terrain allocation errors and device loss. Short two-chunk play succeeded. Surface D3D11 saved-world coverage is limited to the projected path.

**P0**. Measure native target allocation and sustained play at increasing distances. Standard mod route **no**. UWP host bridge **no**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**saves**. The existing backup worlds stayed intact and opened on both ARM devices. Save-write, restart and version migrations need dedicated disjoint copies.

**P0**. Verify edited world state after clean restart and compare save backups. Standard mod route **no**. UWP host bridge **no**.
[world-data.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/world-data.md) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**commands**. Shared Brigadier/game command path is present. Server console teleports used in desktop graphics testing do not verify commands typed in the UWP client.

**P1**. Test client command dispatch, text entry, selectors and command suggestions. Standard mod route **no**. UWP host bridge **no**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md)

**multiplayer**. All three x64 renderer runs joined the same matched scratch server. x86 was verified at title, and the ARM excerpts cover local worlds or title.

**P0**. Join a matching server on x86 and both ARM devices, then verify movement, chat and disconnect. Standard mod route **no**. UWP host bridge **no**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**server-discovery**. The native launcher stores a server list and status, and x64 Direct Connect is proven. Client broadcast discovery, firewall/AppContainer behavior and LAN host/join across devices remain open.

**P1**. Test LAN host discovery and direct joins from a separate machine. Standard mod route **no**. UWP host bridge **conditional**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [README.md](../uwp/pregame/README.md) · [AppxManifest.xml](../uwp/host/AppxManifest.xml)

**play-with-friends**. The bridge disables horizon-client defaults and does not enable the friends feature. Ordinary direct multiplayer is a distinct row.

**P1**. Evaluate socket, relay, firewall and user consent behavior before enabling the feature. Standard mod route **no**. UWP host bridge **conditional**.
[play-with-friends.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/play-with-friends.md) · [Cargo.toml](../uwp/game-bridge/Cargo.toml) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**online-account**. The XAML launcher uses device-code sign-in and Credential Locker. Tests of local mocks and a returned device code do not prove full authenticated play on any current build.

**P1**. Use a consenting supplied account to verify sign-in, refresh and online-mode join. Standard mod route **no**. UWP host bridge **yes**.
[microsoft-account.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/microsoft-account.md) · [README.md](../uwp/pregame/README.md) · [validation.md](../docs/validation.md)

**realms**. Official source contains an availability check and consumers that require a valid session. Neither previous real service evidence nor a target UWP Realms session is recorded. Compare service transport and active screen wiring before marking support.

**P1**. Exercise availability, roster and join in a disposable Realm with a real account. Standard mod route **no**. UWP host bridge **conditional**.
[realms-availability.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/realms-availability.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [README.md](../uwp/pregame/README.md)

**cross-version**. The pinned engine targets Java 26.3. Builder pins a game/asset recipe. The record contains one matched protocol join and does not imply arbitrary Java protocol conversion or Bedrock interoperability.

**P1**. Test exact compatible peers and refusal of mismatched versions. Standard mod route **no**. UWP host bridge **no**.
[version-ports.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/version-ports.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [README.md](../uwp/builder/README.md)

**dedicated**. The signed UWP client packages an integrated server. Its packaging flow does not produce the dedicated-server executable or upstream service/hosting tools.

**P2**. Keep desktop server distribution separate and test client interoperability with it. Standard mod route **no**. UWP host bridge **yes**.
[hosting.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/hosting.md) · [Cargo.toml](../uwp/game-bridge/Cargo.toml) · [README.md](../uwp/builder/README.md)

**packs**. Resource and datapack loading code is shared. Sandboxed discovery/import, per-pack formats and server downloads require explicit tests.

**P1**. Import a disposable ZIP and accept a server pack on desktop and phone. Standard mod route **conditional**. UWP host bridge **conditional**.
[resource-packs.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/resource-packs.md) · [architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [README.md](../uwp/pregame/README.md)

**optifine-resources**. Shared resource modules are carried, but no representative pack pass or individual renderer compatibility matrix exists.

**P2**. Run the documented resource-pack feature fixtures on each primary backend. Standard mod route **conditional**. UWP host bridge **no**.
[resource-packs.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/resource-packs.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**skins**. Shared rendering modules and account path exist. Offline world capture does not exercise online skins, linked Cosmetics or official services.

**P2**. Verify local skin drawing separately from profile services and optional cosmetics. Standard mod route **conditional**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [cosmetics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/cosmetics.md) · [README.md](../uwp/pregame/README.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**extras**. Much of the in-game extras and settings code stays in the shared engine. Pack-migrated extras (FPS, keystrokes, zoom and minimap) add install/manifest/runtime requirements and get no blanket support claim.

**P2**. Verify stock extras individually and install the currently documented official mod equivalents. Standard mod route **yes**. UWP host bridge **conditional**.
[extras.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/extras.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**split-screen**. Upstream exposes the splitscreen extra. The UWP package uses one XAML/CoreWindow host. Split-screen input and rendering were not exercised.

**P2**. Inspect per-seat surfaces and run two local controllers on a standalone profile. Standard mod route **conditional**. UWP host bridge **conditional**.
[extras.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/extras.md) · [README.md](../uwp/pregame/README.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**starling-touch**. The UWP branch uses in-game touch services and its own Winit integration. The Lumia hardware test confirmed menu taps, MOVE and controller/touch handoff.

**P0**. Retest true input routing and controller arbitration on the target Nightly. Standard mod route **conditional**. UWP host bridge **yes**.
[horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-2026-11-18.md](../docs/nightly-2026-11-18.md)

## Graphics

| ID | Feature | x64 11 | x64 12 | x64 GL | x86 11 | x86 12 | x86 GL | A64 11 | A64 12 | A32 11 | A32 12 | A32 GL |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| renderer-dx11 | UWP wgpu D3D11 renderer | L | X | X | L | X | X | L | X | V | X | X |
| renderer-dx12 | Official Windows DirectX 12 backend | X | V | X | X | I | X | X | L | X | H | X |
| renderer-angle | UWP ANGLE over D3D11 with wgpu GLES | X | X | L | X | X | X | X | X | X | X | H |
| renderer-vulkan | Official Windows Vulkan and Auto preference | U | U | U | U | U | U | U | U | U | U | U |
| renderer-metal | Official macOS Metal backend | H | H | H | H | H | H | H | H | H | H | H |
| legacy-native-d3d11 | Earlier separate native D3D11 FL9_1 renderer | D | D | D | D | D | D | D | D | D | D | D |
| graphics-screen | Graphics tabs, search, focus, Undo and dependencies | I | I | I | I | I | X | I | I | I | X | X |
| presets | Full Performance, Performance, Balanced, Quality, Ultra and Custom | L | L | L | L | L | X | L | L | L | X | X |
| graphics-vanilla | Brightness, GUI scale, weather, clouds, particles, smooth lighting, vignette, blend, distances, entities and chunk fade | I | I | I | I | I | X | I | I | L | X | X |
| graphics-lod | Distant terrain, far detail, BlueMap and distant plant dither | I | I | I | I | I | X | I | I | L | X | X |
| graphics-mipmaps | Mipmap levels, texture filtering and anisotropy | L | I | L | L | X | X | L | I | L | X | X |
| fixed-scale | Resolution Scale and native-resolution HUD | U | I | I | U | X | X | U | I | U | X | X |
| dynamic-scale | Dynamic Resolution, minimum scale and target FPS | U | I | X | U | X | X | U | I | U | X | X |
| fxaa | FXAA before HUD | G | I | L | G | X | X | G | I | G | X | X |
| taa | TAA, reprojection, history, TAA upscaling and sharpness | G | I | X | G | X | X | G | I | G | X | X |
| msaa4x | 4× opaque/cutout terrain MSAA including eligible native packs | G | I | G | G | X | X | G | I | G | X | X |
| fsr31 | FSR 3.1 frame generation | G | I | X | G | X | X | G | I | G | X | X |
| hdr | HDR, calibration, black/peak/paper/UI whites and looks | G | I | X | G | X | X | G | I | G | X | X |
| shader-pack-native | Horizon WGSL .hshader packs and pack settings | G | I | L | G | X | X | G | I | G | X | X |
| shader-pack-classic | Iris/OptiFine shader pack compatibility | G | I | L | G | X | X | G | I | G | X | X |
| shadows-post | Pack shadow cache, mirrors, displacement, colored lights and post stages | G | I | L | G | X | X | G | I | G | X | X |
| shader-rays | Horizon pack ray-tracing stages and runtime resources | G | I | X | G | X | X | G | I | G | X | X |
| shader-compute | Pack compute stages and kept resources | G | I | X | G | X | X | G | I | G | X | X |
| vanilla-post | Menu background blur, entity outlines, spectator and camera post effects | G | I | L | G | X | X | G | I | G | X | X |
| improved-transparency | Vanilla improved transparency and translucent batching | L | I | L | L | X | X | L | I | L | X | X |
| gpu-culling | GPU-driven terrain culling, Auto and indirect-count draws | G | G | G | G | X | X | G | G | G | X | X |
| occlusion-visibility | GPU occlusion and experimental visibility buffer | X | X | X | G | X | X | G | X | G | X | X |
| cpu-culling | Cave, frustum, entity, particle, leaf culling and precheck | I | I | I | I | I | X | I | I | I | X | X |
| block-entity-caches | Shulkers, signs, pots, bells, chests, banners and statues | I | I | I | I | I | X | I | I | I | X | X |
| screenshots | F2, screenshot quality, world icon capture and new mod screenshot request | L | L | L | I | L | X | L | L | L | X | X |
| vsync | VSync, present mode, limiter and frame pacing | L | L | L | I | L | X | L | L | L | X | X |
| fullscreen | Borderless fullscreen, exclusive fullscreen and mode selection | L | L | L | L | L | X | L | L | L | X | X |
| gpu-timer | GPU query timestamps, pass timings and performance overlay | L | I | X | L | X | X | L | I | L | X | X |
| advanced-buffers | Texture arrays, floating-point depth, separate alpha and physical texture limits | L | I | X | L | X | X | L | I | L | X | X |

**renderer-dx11**. The packaged D3D11 path projects the FL9_1 compatibility renderer even on hardware with a higher feature level. Matched x64 multiplayer, x86 title, Surface title and projected world, and strict physical FL9_1 Lumia world are separately documented.

**P0**. Repeat title and world gates on the refreshed signed artifacts. Standard mod route **no**. UWP host bridge **yes**.
[RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**renderer-dx12**. x64 rendered matched multiplayer. Surface signed DX12 ran the title and both hooks, with no world proof in that run. x86 builds can include it, with no matching game evidence. The Lumia host rejected the available ARM32 DX12 adapter.

**P0**. Verify x86 and Surface world plus valid timing on hardware DX12. Standard mod route **no**. UWP host bridge **yes**.
[RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [nightly-surface-2026-11-20.md](../docs/nightly-surface-2026-11-20.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-2026-11-18.md](../docs/nightly-2026-11-18.md)

**renderer-angle**. x64 signed package rendered a matched GLES3 multiplayer scene. It needs pinned DLLs and an EGL window configuration. No staged x86 or ARM64 runtime is verified. Lumia ANGLE surface probe failed. The evidence does not settle alternative driver paths.

**P2**. Package and test the available alternatives separately. Keep x64 ANGLE explicitly experimental. Standard mod route **no**. UWP host bridge **yes**.
[RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**renderer-vulkan**. UWP build excludes wgpu Vulkan. The XAML launcher picks a packaged backend independently of vanilla graphics API preferences.

**P2**. Assess a verified AppContainer-compatible Vulkan host/backend before exposing the choice. Standard mod route **no**. UWP host bridge **yes**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [Cargo.toml](../uwp/game-bridge/Cargo.toml)

**renderer-metal**. Metal is an Apple platform API. UWP uses Windows presentation backends.

**P3**. No Windows implementation of the Metal backend is required. Standard mod route **no**. UWP host bridge **yes**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md)

**legacy-native-d3d11**. The legacy standalone renderer sources are retained. Nightly selects renderer 3 and omits renderer 0 from packaged builds.

**P3**. Keep documentation aligned with package feature flags. Standard mod route **no**. UWP host bridge **yes**.
[RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md) · [Cargo.toml](../uwp/game-bridge/Cargo.toml) · [nightly-2026-11-18.md](../docs/nightly-2026-11-18.md)

**graphics-screen**. Shared screen code is carried. Capability-gated rows may be shown even where the old renderer discards their values.

**P1**. Test setting navigation on touch and controller and explain unavailable options at point of use. Standard mod route **no**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**presets**. Preset data is shared. The FL9_1 compatibility path ignored world scale and post effects, and high desktop distances are unsuitable for the observed phone profile.

**P1**. Keep official settings and verify safe device-specific defaults and capability hints. Standard mod route **no**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**graphics-vanilla**. Shared options and renderer inputs remain. FL9_1 uses its own terrain/entity/HUD shaders and bounded atlas resources. The Lumia's short run does not enumerate each setting or validate desktop presets under memory pressure.

**P1**. A/B test representative option families and mobile settings load/save. Standard mod route **conditional**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**graphics-lod**. The port retains related source. Memory limits and exact visual support for every FL9_1 shader option are unmeasured. Existing desktop scene matches use no far-terrain stress probe.

**P2**. Verify each option, then stress a moderate LOD case at matched settings. Standard mod route **conditional**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**graphics-mipmaps**. FL9_1 packs tiles into bounded sheets and suppresses runtime anisotropy even though sampler settings remain. Modern paths depend on adapter filtering capability.

**P2**. Expose the FL9_1 limitation and test mipmap/filter distinctions independently. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**fixed-scale**. The baseline FL9_1 set_world_post returns before applying scale. The Lumia still presented 2561×1439 at its 50% setting. DX12 and GLES retain the ordinary world-post path. No fixed-scale measurement on those UWP configurations is in the baseline.

**P0**. Instrument world target versus surface dimensions and verify four fixed scales before adaptive policy. Standard mod route **no**. UWP host bridge **yes**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [graphics.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-render/src/graphics.rs) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**dynamic-scale**. Upstream derives steps from measured GPU frame time and applies them through world post. FL9_1 has neither the baseline scaled world target nor valid timings. GLES needs a timing-capability probe.

**P0**. Verify actual dimensions, labeled timing and stable changes in paired heavy/light scenes. Standard mod route **no**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [dynamic_resolution.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-render/src/dynamic_resolution.rs) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**fxaa**. The FL9_1 world-post pipeline is disabled. DX12 retains the path. ANGLE behavior depends on the specific adapter and working render targets.

**P2**. Test capability and keep FL9_1 menu truthful. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**taa**. FL9_1 disables world post. Modern paths need suitable scene and depth textures. The target adds TAA upscaling under compatible native post-free shader packs.

**P2**. Verify native DX12 and scope ANGLE support via feature checks first. Standard mod route **no**. UWP host bridge **no**.
[Official releases](https://github.com/horizon-rust/horizon/releases/tag/nightly) · [graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**msaa4x**. The official target adds terrain multisampling and explicitly gates OpenGL. FL9_1 baseline predates this implementation and cannot be assumed to satisfy its resolve path. Modern UWP DX12 has source-level feasibility, pending post-migration tests.

**P2**. Verify the target's resolve formats on DX12, with its documented single-sample fallback. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [Official releases](https://github.com/horizon-rust/horizon/releases/tag/nightly) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**fsr31**. Existing FL9_1 capability gate forces it off. Modern UWP paths retain upstream source. There is no enabled-mode capture in the matched runs. The FL9_1 mission does not add this path.

**P3**. Retain the gate and test capable backends independently of DRS. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md)

**hdr**. FL9_1 suppresses HDR output selection. Matched desktop runs used SDR. Modern backend swapchain format and OS display mode determine eligibility.

**P2**. Verify in an HDR-capable Windows session and keep SDR behavior separately verified. Standard mod route **no**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [hdr-passes.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/hdr-passes.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md)

**shader-pack-native**. FL9_1 rejects native shader packs. Modern renderers retain loader/pipeline code. OpenGL needs per-pack capability checks.

**P2**. Run a representative stock pack on DX12 and an eligible GLES pack. Standard mod route **conditional**. UWP host bridge **no**.
[shaders.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/shaders.md) · [graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**shader-pack-classic**. FL9_1 explicitly rejects the pack. Upstream's decoder does not imply support for every Iris/OptiFine pack. No complete signed UWP shader-pack run is recorded.

**P2**. Verify terrain, shadows and composite outputs with a known fixture on eligible backends. Standard mod route **conditional**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [shaders.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/shaders.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**shadows-post**. These are pack-selected paths on eligible renderers. FL9_1 pack rejection and strict sampling/shader limits constrain them. Ordinary entity shadow sprites are separate.

**P2**. Report availability per stage and backend rather than per pack extension alone. Standard mod route **conditional**. UWP host bridge **no**.
[shaders.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/shaders.md) · [graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**shader-rays**. The official shader-pack API describes rays and layers. This is not evidence of DXR support. FL9_1 cannot use the pack pipeline. Modern paths require the pack's declared capabilities and a runtime test.

**P3**. Validate a documented pack scene only on a compatible backend. Standard mod route **conditional**. UWP host bridge **no**.
[shaders.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/shaders.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**shader-compute**. The pack path depends on declared/adaptor support for compute and storage. FL9_1 rejects the pack and lacks compute. Actual GLES support depends on context.

**P2**. Check pipeline feature flags before making stages selectable. Standard mod route **conditional**. UWP host bridge **no**.
[shaders.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/shaders.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md)

**vanilla-post**. Baseline gfx disables entity outlines and skips menu blur and spectator post on FL9_1. Default terrain and HUD draw independently.

**P2**. Verify modern effects and show unsupported FL9_1 effects as unavailable. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md)

**improved-transparency**. Transparency and its renderer paths depend on format/blend support. FL9_1 couples alpha blending and uses compatibility shaders. Exact OIT behavior is unverified.

**P2**. Use fixed overlapping translucent fixtures across backends. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**gpu-culling**. Official docs require compute and reliable counted indirect draws, and exclude DX12 and GL. FL9_1 does not expose the necessary execution path. CPU visibility remains separate.

**P2**. Verify disabled/Auto UI and CPU fallback on signed packages. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**occlusion-visibility**. Compute and depth-path capabilities differ. FL9_1 lacks the modern world transform and compute paths for the experimental visibility buffer. The baseline evidence does not validate DX12/ANGLE eligibility for every occlusion mode.

**P3**. Inventory exact per-mode adapter gates before exposing experimental toggles. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**cpu-culling**. The shared engine carries these CPU paths. Async modes, benchmarks and third-party resource-pack compatibility have not been physically compared on the new builds.

**P2**. Test default and stressed world scenes with source-derived assertions. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**block-entity-caches**. The shared sources contain independent cache switches. Completed renderer proof does not establish each invalidation, animation or low-memory branch.

**P2**. Exercise a scene with the documented block entities and resource reload. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**screenshots**. The signed Lumia game reached the mod screenshot request and logged access denied. A signed storage probe isolated the denied handle-to-path lookup and verified that the same-directory move works with the validated paths. Patch 0.1.15 uses that path. The corrected game still needs a direct F2 and mod-capture run. [Lumia evidence](nightly-lumia-drs-2026-11-21.md)

**P1**. Verify F2 and mod hook in isolated LocalState, normal and high-resolution modes. Standard mod route **yes**. UWP host bridge **conditional**.
[Official releases](https://github.com/horizon-rust/horizon/releases/tag/nightly) · [graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**vsync**. x64 measured ~60 app Present starts/s with interval 1 in each renderer. That measures application submission, with no display scanout evidence. ARM reports FIFO at startup. Other modes and refresh changes need tests.

**P1**. Verify toggle/state persistence and collect display cadence evidence separately. Standard mod route **no**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [nightly-surface-2026-11-20.md](../docs/nightly-surface-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**fullscreen**. The UWP host controls fullscreen through Windows application view APIs. It presents using its XAML/CoreWindow composition path. There is no verified exclusive mode switch or refresh negotiation in the port.

**P2**. Test borderless resizing, verify explicit exclusive-mode UI rejection if unavailable. Standard mod route **no**. UWP host bridge **yes**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md) · [window.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-input/src/window.rs) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**gpu-timer**. Compatibility renderers must show GPU n/a when queries are absent. Lumia physically did. Modern backends need adapter feature and valid sample verification. App Present intervals and CPU measurements are distinct metrics.

**P0**. Check sample validity and truthful labeled DRS fallback on each hardware profile. Standard mod route **no**. UWP host bridge **no**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md)

**advanced-buffers**. FL9_1 substitutes packed sheets, Depth24Plus and coupled blending. It bounds cube and ordinary 2D texture size. HAL replay passed the retained depthless HUD path on ARM. Modern pipelines have independent feature limits.

**P0**. Verify scaled render targets stay within adapter texture and memory limits. Standard mod route **no**. UWP host bridge **yes**.
[horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

## Input and accessibility

| ID | Feature | x64 11 | x64 12 | x64 GL | x86 11 | x86 12 | x86 GL | A64 11 | A64 12 | A32 11 | A32 12 | A32 GL |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| keyboard | Keyboard movement, gameplay keys, shortcuts and remapping | I | I | I | I | I | X | I | I | I | X | X |
| mouse | Absolute cursor, raw deltas, look, capture, scroll and sensitivity | I | I | I | I | I | X | I | I | I | X | X |
| clipboard | Clipboard copy/paste in applicable editing fields | I | I | I | I | I | X | I | I | I | X | X |
| ime | Text composition, IME, chat entry and Windows touch keyboard | X | X | X | X | X | X | X | X | X | X | X |
| touch | Touch menu taps, MOVE, camera, HUD and input switching | I | I | I | I | I | X | I | I | L | X | X |
| gamepad | Controller sticks, bindings, glyphs, face button swap, radial menu | I | I | I | I | I | X | I | I | L | X | X |
| pad-keyboard | Controller on-screen keyboard and UI navigation | I | I | I | I | I | X | I | I | I | X | X |
| pad-hotplug | Gamepad attach, removal, reconnect and focus handling | I | I | I | I | I | X | I | I | I | X | X |
| pad-rumble | Controller rumble and device vibration | I | I | I | I | I | X | I | I | I | X | X |
| dual-sense | DualSense adaptive triggers, shape haptics, lightbar and LEDs | U | U | U | U | U | U | U | U | U | U | U |
| xbox-impulse | Xbox One impulse trigger effects | U | U | U | U | U | U | U | U | U | U | U |
| debug-f3 | F3 debug visibility, overlays, touch button and bindable pad action | L | L | L | I | L | X | L | L | V | X | X |
| narrator | Game narration and screen focus announcements | U | U | U | U | U | U | U | U | U | U | U |
| accessibility | Accessibility options, contrast, text scaling, toggle controls and screen focus | I | I | I | I | I | X | I | I | I | X | X |
| gyro | Gyroscope camera aim as a standard Horizon mod | U | U | U | U | U | U | U | U | L | U | U |

**keyboard**. The port owns a Winit/CoreWindow event path and retains shared binding logic. The native keyboard correction in the product main branch predates this Nightly package series. Its validation remains separate from exact current build evidence.

**P0**. Verify gameplay keys, rebinding, shortcuts and held-key release on focus changes. Standard mod route **yes**. UWP host bridge **yes**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [controllers.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/controllers.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-2026-11-18.md](../docs/nightly-2026-11-18.md)

**mouse**. The UWP Winit/window bridge owns the OS event feed. The shared client retains menu/control routing. Screenshots do not validate mouse capture and raw input.

**P0**. Exercise focus loss, relative camera and wheel on each desktop and tablet backend. Standard mod route **yes**. UWP host bridge **yes**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [mouse.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-input/src/mouse.rs) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**clipboard**. UWP uses the platform branch of the clipboard code. Test Unicode and clipboard startup behavior with the real desktop and phone UI.

**P1**. Copy/paste in chat, world name, and multiplayer address fields. Standard mod route **no**. UWP host bridge **yes**.
[clipboard.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-input/src/clipboard.rs) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**ime**. Shared screens and UWP input exist. A touchscreen menu tap is not a text composition test. The latest Lumia report leaves on-screen keyboard coverage open.

**P1**. Verify composition/commit with a second language and open the OS keyboard from chat. Standard mod route **no**. UWP host bridge **yes**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**touch**. Lumia hardware verified menu taps, movement and switch-away-on-GameSir. Inventory, text entry and every gesture have no equivalent coverage.

**P0**. Repeat filmed tests for menus, look, movement, inventory, F3 and handoff. Standard mod route **conditional**. UWP host bridge **yes**.
[horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**gamepad**. UWP selects its gilrs/platform bridge. Shared mappings remain. GameSir input/handoff is physically proved and mapping/save/load corrections have automated coverage. Individual controller families and radial bindings are unverified.

**P0**. Test GameSir and representative Windows pad families, reload settings and validate F3. Standard mod route **yes**. UWP host bridge **yes**.
[controllers.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/controllers.md) · [Cargo.toml](../uwp/game-bridge/Cargo.toml) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**pad-keyboard**. Shared pad UI and custom keyboard sources remain. The Lumia report leaves this case open and Surface title automation used XAML UI Automation.

**P1**. Enter chat and dismiss keyboard on all form factors. Standard mod route **conditional**. UWP host bridge **conditional**.
[controllers.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/controllers.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-surface-2026-11-20.md](../docs/nightly-surface-2026-11-20.md)

**pad-hotplug**. Windows.Gaming.Input and the UWP bridge retain lifecycle paths, but the successful handoff run does not prove disconnect, hotplug, reconnect or background edge handling.

**P1**. Disconnect a pad mid-input and verify release and reconnect. Standard mod route **no**. UWP host bridge **yes**.
[controllers.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/controllers.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**pad-rumble**. A separate Lumia device vibration module has built and passed focused tests, but motor output or standard controller rumble was not physically confirmed.

**P2**. Observe phone vibration and controller motors on known supported devices. Standard mod route **conditional**. UWP host bridge **yes**.
[controllers.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/controllers.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-2026-11-18.md](../docs/nightly-2026-11-18.md)

**dual-sense**. Upstream HID extensions are behind the desktop controller dependency. The default UWP game bridge enables uwp-controller instead. The manifest's generic gamepad declaration is not an implementation of those control writes.

**P2**. Map the supported Windows HID APIs to a narrowly scoped backend with a real DualSense. Standard mod route **no**. UWP host bridge **yes**.
[controllers.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/controllers.md) · [Cargo.toml](../uwp/game-bridge/Cargo.toml) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [AppxManifest.xml](../uwp/host/AppxManifest.xml)

**xbox-impulse**. Uses the upstream optional HID output crate excluded by the UWP controller feature set.

**P2**. Evaluate Windows-compatible actuator control with actual hardware. Standard mod route **no**. UWP host bridge **yes**.
[controllers.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/controllers.md) · [Cargo.toml](../uwp/game-bridge/Cargo.toml) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**debug-f3**. Stock F3/overlay content is available. Lumia implements a touch F3 action and controller binding. Debug output shows unavailable GPU timing correctly.

**P1**. Verify default/unassigned behavior and enable/disable through all three input routes. Standard mod route **yes**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md)

**narrator**. The port supplies an inactive narrator instead of using unavailable desktop SAPI. This is a known port gap, with no claim about possible WinRT speech alternatives.

**P1**. Prototype UWP speech synthesis and verify screen and text narration. Standard mod route **conditional**. UWP host bridge **yes**.
[narrator.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/narrator.md) · [README.md](../uwp/pregame/README.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**accessibility**. Stock options and TTS-independent controls are present. Narration and input platform paths have their own rows. There is no per-option hardware accessibility suite.

**P1**. Exercise stock accessibility options using keyboard, controller and touch. Standard mod route **conditional**. UWP host bridge **conditional**.
[architecture.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md) · [controllers.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/controllers.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**gyro**. Baseline already has an opt-in Gyrometer host/internal input integration, with no physical aim evidence. No standalone standard gyro aiming mod is part of the baseline. The planned mod should use a narrowed host provider and normal camera path.

**P0**. Test actual yaw/pitch, game focus, disable/re-enable and orientation on Lumia. Standard mod route **yes**. UWP host bridge **yes**.
[nightly-2026-11-18.md](../docs/nightly-2026-11-18.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [Gyrometer.cpp](../uwp/gyro-bridge/host/Gyrometer.cpp) · [lib.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-mod-api/src/lib.rs)

## Audio

| ID | Feature | x64 11 | x64 12 | x64 GL | x86 11 | x86 12 | x86 GL | A64 11 | A64 12 | A32 11 | A32 12 | A32 GL |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| music | Music, menus and in-world track playback | I | I | I | I | I | X | I | I | V | X | X |
| sound-effects | Sound effects and category/master volume | I | I | I | I | I | X | I | I | L | X | X |
| music-stream | Bounded music decode/streaming, loop, pause and end-of-stream | I | I | I | I | I | X | I | I | V | X | X |
| positional-audio | Spatial attenuation, stereo panning, listener updates and mod sound changes | I | I | I | I | I | X | I | I | I | X | X |
| audio-routing | Explicit device picker, switching and hotplug | U | U | U | U | U | U | U | U | U | U | U |

**music**. The signed Lumia 1.0.0.8 run produced audible menu music. ARM64 mod captures used an audio-disabled fixture. Desktop parity tests did not verify audio.

**P0**. Record in-world music and menu transitions with sound enabled on every native target. Standard mod route **conditional**. UWP host bridge **yes**.
[horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-surface-2026-11-20.md](../docs/nightly-surface-2026-11-20.md)

**sound-effects**. The user heard sound effects during the phone test. The same shared mixer owns volume. Exact setting tests and a cross-architecture audio run are missing.

**P0**. A/B master and category controls and confirm no startup mute or sound dropouts. Standard mod route **conditional**. UWP host bridge **yes**.
[horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-surface-2026-11-20.md](../docs/nightly-surface-2026-11-20.md)

**music-stream**. The adapted game decodes into a bounded ~480 KiB stereo PCM queue. The earlier whole-track decode could consume ~61 MiB. Six focused tests and audible phone playback establish this baseline route.

**P0**. Preserve bounded streaming and measure continuity during world entry and DRS. Standard mod route **conditional**. UWP host bridge **yes**.
[horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**positional-audio**. Shared mixer plus UWP XAudio2 default output are carried. Audible SFX do not establish position/distance or every mod sound path.

**P1**. Play known left/right/distant sources and an isolated mod event. Standard mod route **conditional**. UWP host bridge **conditional**.
[horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md)

**audio-routing**. The port enables UWP XAudio2 instead of desktop CPAL. Non-CPAL output_devices returns an empty list and set_device stores the name without switching. The default XAudio2 device works on the Lumia.

**P1**. Add WinRT/compatible endpoint enumeration and explicit XAudio2 switching where possible. Standard mod route **no**. UWP host bridge **yes**.
[Cargo.toml](../uwp/game-bridge/Cargo.toml) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [audio.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-render/src/audio.rs)

## Mods

| ID | Feature | x64 11 | x64 12 | x64 GL | x86 11 | x86 12 | x86 GL | A64 11 | A64 12 | A32 11 | A32 12 | A32 GL |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| content-mod | Standard .hmod content registries, dependencies and prepared mod assets | L | I | I | L | I | X | I | I | I | X | X |
| wasm-mod | Standard Wasm .hmod manifest, loader, guest ABI and shared stdlib | L | I | I | L | I | X | L | L | L | X | X |
| wasm-safety | Wasm dependency checks, hash/ABI identity, traps, fuel and memory limits | L | I | I | I | X | L | I | I | I | X | X |
| wasm-limits | Wasm bundled JIT versus interpreted AppContainer execution | L | L | L | L | L | X | L | L | L | X | X |
| wasm-toggle | Mods enabled state, toggle persistence and restart | V | I | I | V | I | X | I | I | L | X | X |
| native-source | Trusted standard native Rust sources, exact SDK and engine build identity | L | L | L | L | L | X | L | L | L | X | X |
| native-packaged | Independently shipped signed native DLLs with related-set graph | L | I | I | L | I | X | L | L | U | X | X |
| native-embedded | Builder-time embedded native code and runtime hook registration | I | I | I | I | I | X | I | I | L | X | X |
| native-arbitrary | Load desktop-built native mods or standalone mod DLLs from writable folders | U | U | U | U | U | U | U | U | U | U | U |
| native-settings | Standard mod settings, configuration screen and data persistence | I | I | I | I | I | X | I | I | L | X | X |
| mod-actions | Public client hook families, actions, input, GUI, screens, network, sound and local actor | L | I | I | L | I | X | L | L | L | X | X |
| mod-ren | Mod pipeline registration, native shader APIs, custom renderers and advanced drawing | L | I | I | L | I | X | L | I | L | X | X |
| mod-mixins | Native registration, generated injections, slots, advanced mixin-style hooks and unsafe trust boundaries | L | L | L | L | L | X | L | L | L | X | X |
| new-game-api | Target API changes for profiles, stats, structures and diagnostics | X | X | X | X | X | X | X | X | X | X | X |
| mod-restart | Dependency ordering, missing/different package state, saved-world mismatch, continued runtime | L | I | I | L | I | X | I | I | L | X | X |
| mod-browser | Official catalog, native bundle trust and launcher-driven install/update | L | L | L | L | L | X | L | L | L | X | X |

**content-mod**. The bounded copier and registry rewriting were tested with a standard entity fixture. Signed x86 D3D11 loaded it alongside both code fixtures and retained the registry ID, command entry and shadow data. ARM content behavior did not get the same fixture proof.

**P0**. Load and spawn the same content entity in-world on all four native targets. Standard mod route **yes**. UWP host bridge **no**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [content-staging-2026-11-19.md](../docs/content-staging-2026-11-19.md)

**wasm-mod**. Signed fixtures really registered and drew the typed GUI hook on desktop x64/x86 and both ARM devices with Pulley. Execution is revision/ABI-specific and target stdlib requires rebuild validation.

**P0**. Build exact target ABI fixtures after migration and prove loaded hook/payload in-world. Standard mod route **yes**. UWP host bridge **no**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md) · [nightly-surface-2026-11-20.md](../docs/nightly-surface-2026-11-20.md)

**wasm-safety**. Signed x64 proved package rejection and a real hook trap, followed by another rendered frame. Focused AppContainer probes covered bounds and fuel, which still need the same end-to-end coverage on each architecture.

**P0**. Run malformed and valid local fixtures on each candidate, and verify dependent group behavior. Standard mod route **yes**. UWP host bridge **no**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [README.md](../uwp/wasm-abi-probe/README.md)

**wasm-limits**. UWP deliberately uses Pulley without code-generation capability. ARM32 carries the low-memory override and needs cold-start workload measurements. A desktop JIT binary/native runtime is not interchangeable with the UWP package.

**P1**. Keep no-JIT route and measure memory, cold startup and failure behavior independently. Standard mod route **no**. UWP host bridge **conditional**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [README.md](../uwp/code-generation-probe/README.md)

**wasm-toggle**. Signed x64/x86 preserved exact marker counts across enabled, disabled and re-enabled runs. Lumia first in-process restart hit a global registration seal and required a new process. ARM toggle sequence is unverified.

**P0**. Retest x64/x86 toggles and complete clean process restart behavior on Surface and Lumia. Standard mod route **yes**. UWP host bridge **no**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**native-source**. Builder compiles trusted source against the patched game, produces metadata/inventory and validates dependency order. Upstream native binaries are not interchangeable with UWP build identity.

**P0**. Regenerate the SDK closure and rebuild each fixture against exact Nightly source. Standard mod route **yes**. UWP host bridge **no**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [lib.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-mod-api/src/lib.rs) · [native-mods-uwp-2026-11-19.md](../docs/native-mods-uwp-2026-11-19.md) · [Cargo.toml](../uwp/game-bridge/Cargo.toml) · [README.md](../uwp/builder/README.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**native-packaged**. Signed x64, x86 and ARM64 used main/optional related sets. Lumia uses embedded trusted code. Phone executable optional-package support is beyond retail Mobile's OS floor, and the baseline build excludes ARM32 packaged loading.

**P0**. Verify architecture and SDK checks, signed versions, enable/disable and a new package on each supported device. Standard mod route **yes**. UWP host bridge **yes**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [native-mods-uwp-2026-11-19.md](../docs/native-mods-uwp-2026-11-19.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md)

**native-embedded**. ARM32 signed package proved typed GUI in-world from a compiled-in fixture. Rebuild is required to change embedded code. For x86/x64 and ARM64 the independent signed route also exists.

**P0**. Embed the gyro mod and verify native/wasm coexistence in the same world. Standard mod route **yes**. UWP host bridge **conditional**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [lib.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-mod-api/src/lib.rs) · [native-mods-uwp-2026-11-19.md](../docs/native-mods-uwp-2026-11-19.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md)

**native-arbitrary**. The UWP host intentionally uses signed package graph or trusted embedding. Related-set loader proof required package relationship metadata, even for a DLL whose package already appeared in the graph.

**P1**. Keep the signed/embedded routes and explain the required build identities. Standard mod route **no**. UWP host bridge **yes**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [native-mods-uwp-2026-11-19.md](../docs/native-mods-uwp-2026-11-19.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**native-settings**. Mods configuration and enabled lists are carried. Only enabled-state persistence on x86/x64 plus person-driven package import on Lumia has detailed evidence. Complete per-setting host capabilities need fixture coverage.

**P1**. Persist a typed config including defaults and invalid values across isolated launches. Standard mod route **yes**. UWP host bridge **conditional**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**mod-actions**. Typed GUI hook execution is verified on all native targets in the old packages. Other documented families and portable/native-only/side restrictions need compatible fixtures. The exact target introduces graceful quit and screenshot calls.

**P1**. Exercise exact target hook families in world, including new screenshot and quit calls. Standard mod route **yes**. UWP host bridge **conditional**.
[Official releases](https://github.com/horizon-rust/horizon/releases/tag/nightly) · [SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [lib.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-mod-api/src/lib.rs) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**mod-ren**. The portable GUI hook works on FL9_1. Advanced rendering and .hshader stages have different caps. No package's GUI marker establishes arbitrary pipelines, scene access or shader compatibility.

**P2**. Test a typed custom renderer without replacing stock drawing and separately test each allowed backend. Standard mod route **yes**. UWP host bridge **conditional**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [lib.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-mod-api/src/lib.rs) · [shaders.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/shaders.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**mod-mixins**. Trusted Rust and generated engine contracts exist, with different paths for attached native DLLs and embedded packages. Wasm is limited to the portable contract. No broad fixture coverage proves advanced injections on UWP.

**P1**. Build architecture-specific registration plus representative mutation/injection fixtures. Standard mod route **yes**. UWP host bridge **conditional**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [lib.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-mod-api/src/lib.rs) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [native-mods-uwp-2026-11-19.md](../docs/native-mods-uwp-2026-11-19.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**new-game-api**. The pinned release introduces these calls. No previous package can verify target APIs, and API source presence alone cannot establish every consumer or Wasm binding.

**P1**. Regenerate and compare typed signatures and guest capability catalogue, then exercise representative calls. Standard mod route **yes**. UWP host bridge **conditional**.
[Official releases](https://github.com/horizon-rust/horizon/releases/tag/nightly) · [lib.rs](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-mod-api/src/lib.rs) · [SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md)

**mod-restart**. x64 related-set dependency ordering is verified and logs confirm invalid Wasm group rejection. Lumia requires a fresh process after certain returns to the launcher. Saved-world mismatch and interoperation need dedicated test fixtures.

**P0**. Check dependency/version failures and safe restart/reopen without mutating original saves. Standard mod route **yes**. UWP host bridge **no**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**mod-browser**. Builder has registry browsing and trusted native source selection. UWP has import routes. No proof of the desktop launcher's full catalogue/update, author trust, or server modpack export workflow on any UWP cell.

**P2**. Document available routes and test known public catalog content with explicit fixture versions. Standard mod route **conditional**. UWP host bridge **conditional**.
[SUMMARY.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/modding/src/SUMMARY.md) · [launcher-commands.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/launcher-commands.md) · [README.md](../uwp/builder/README.md) · [native-mods-uwp-2026-11-19.md](../docs/native-mods-uwp-2026-11-19.md) · [validation.md](../docs/validation.md)

## Platform, performance and diagnostics

| ID | Feature | x64 11 | x64 12 | x64 GL | x86 11 | x86 12 | x86 GL | A64 11 | A64 12 | A32 11 | A32 12 | A32 GL |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| uwp-package | Signed client deployment, pinned assets framework and platform dependencies | L | L | L | L | X | L | L | L | L | X | X |
| sandbox | AppContainer filesystem, installation resources and writable LocalState | L | L | L | L | X | L | L | L | L | X | X |
| file-pickers | Import files through native pickers and locate/log world data | I | I | I | I | I | X | I | I | L | X | X |
| uri | Open external HTTPS links and launcher actions | I | I | I | I | I | X | I | I | I | X | X |
| lifecycle | Activate, suspend, resume, resize, visibility, orientation and save on exit | I | I | I | I | I | X | I | I | L | X | X |
| multiwindow | Multiple windows, launcher/game separation and independent app views | L | L | L | L | L | X | L | L | H | X | X |
| storage | Storage budgets, resource/cache placement and clean error on exhaustion | L | L | L | L | L | X | L | L | L | X | X |
| background | Background activities, launcher refresh, live tiles and shortcuts | L | L | L | L | L | X | L | L | L | X | X |
| desktop-launcher | Official launcher version management, modpacks, CLI/MCP, provisioning and desktop shortcuts | L | L | L | L | L | X | L | L | L | X | X |
| agent-control | In-game horizon-ctl command/control socket and desktop launcher MCP | L | L | L | L | L | X | L | L | L | X | X |
| cpu-times | CPU timing, frame-time overlay, diagnostic metrics and worker pools | L | L | L | I | L | X | L | L | L | X | X |
| perf-policies | Worker count, upload budgets, battery mode, and parallel tick | I | I | I | I | I | X | I | I | L | X | X |
| benchmark | Standard benchmark, profiling, capture/replay and diagnostics export | L | L | L | L | L | X | L | L | L | X | X |
| crashes | Local crash/panic reports, graphics fallback and recovery | L | L | L | L | L | X | L | L | L | X | X |
| telemetry | Consent, activity heartbeat, crash/benchmark reporting and platform identity | I | I | I | I | I | X | I | I | I | X | X |
| shader-cache | Pipeline/shader compilation, persistence and cold/first startup cost | L | L | L | L | L | X | L | L | L | X | X |

**uwp-package**. Signed x64/x86/ARM64/ARM32 development installations exist with distinct evidence. Neutral asset framework reuse and strict link are packaging steps. These are prior artifacts. November 20 signatures and install proof require new packages.

**P0**. Strict-link, sign, verify hashes and install each candidate without touching original identities. Standard mod route **no**. UWP host bridge **yes**.
[README.md](../uwp/builder/README.md) · [AppxManifest.xml](../uwp/host/AppxManifest.xml) · [nightly-2026-11-18.md](../docs/nightly-2026-11-18.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md)

**sandbox**. Host resolves packaged assets through the package graph, stores mutable files in isolated data and patches filesystem access for legacy Mobile. Arbitrary desktop paths and roaming data are not equivalent. Previous font fallback was necessary.

**P0**. Verify packaged reads, LocalState writes, font fallback and out-of-sandbox error handling. Standard mod route **conditional**. UWP host bridge **yes**.
[README.md](../uwp/builder/README.md) · [README.md](../uwp/pregame/README.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-arm-hud-2026-11-20.md](../docs/nightly-arm-hud-2026-11-20.md) · [nightly-2026-11-18.md](../docs/nightly-2026-11-18.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**file-pickers**. The phone user imported the standard .hmod through its launcher picker. No generic parity claim applies to directories, all resource pack types or external save import/export.

**P1**. Round-trip worlds, mods, packs and a report using platform picker APIs. Standard mod route **conditional**. UWP host bridge **yes**.
[README.md](../uwp/pregame/README.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-2026-11-18.md](../docs/nightly-2026-11-18.md)

**uri**. The shell uses Windows.System.Launcher.LaunchUriAsync. The gaming path and app availability on Mobile still matter.

**P2**. Open a public link and return without game/session loss. Standard mod route **conditional**. UWP host bridge **yes**.
[XamlShell.cpp](../uwp/pregame/host/XamlShell.cpp) · [README.md](../uwp/pregame/README.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**lifecycle**. CoreWindow handlers and renderer surface lifetime code are carried. Mobile landscape and device launch exist. Repeated suspend/resume and resize recovery are open.

**P0**. Suspend during world play, resume, rotate and quit on isolated packages with stage logging. Standard mod route **no**. UWP host bridge **yes**.
[App.cpp](../uwp/host/App.cpp) · [RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-surface-2026-11-20.md](../docs/nightly-surface-2026-11-20.md)

**multiwindow**. Pre-game and game intentionally share one app view in the normal host. Retail Mobile has a single-window model. The port's one-window UX is a cross-device packaging choice.

**P2**. Assess Windows desktop multi-view independently of Mobile. Standard mod route **no**. UWP host bridge **yes**.
[README.md](../uwp/pregame/README.md) · [RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md) · [App.cpp](../uwp/host/App.cpp)

**storage**. Private asset staging uses bounded copying and exact resource identities. Mobile has real terrain/GPU allocation pressure. Builder prior builds reached disk error 112 on multiple volumes.

**P0**. Reserve dedicated C: scratch, verify free space and measure windowed phone memory under steady play. Standard mod route **no**. UWP host bridge **yes**.
[README.md](../uwp/builder/README.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch)

**background**. The XAML launcher refreshes status while running and supports tile metadata. No background-service gameplay or updater equivalent is delivered by that refresh path.

**P2**. Verify live tile activation and status on supported Windows versions. Standard mod route **conditional**. UWP host bridge **yes**.
[README.md](../uwp/pregame/README.md) · [AppxManifest.xml](../uwp/host/AppxManifest.xml)

**desktop-launcher**. The separate Windows Builder and XAML shell divide setup, packaging, version selection and startup work. They do not duplicate all desktop install/upgrade, pack and CLI flows.

**P2**. Map comparable Builder actions and identify remaining high-value launcher gaps. Standard mod route **conditional**. UWP host bridge **yes**.
[launcher-commands.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/launcher-commands.md) · [agent-control.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/agent-control.md) · [README.md](../uwp/builder/README.md) · [README.md](../uwp/pregame/README.md)

**agent-control**. Shared in-game control sources ship, and prior x64 automation obtained game screenshot/camera evidence. The desktop launcher MCP/CLI and a same-device AppContainer client transport have separate constraints. Device Portal and UI Automation are separate mechanisms.

**P2**. Verify local scratch automation through supported UWP transport and report the allowed command subset. Standard mod route **conditional**. UWP host bridge **yes**.
[agent-control.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/agent-control.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [README.md](../uwp/pregame/README.md) · [AppxManifest.xml](../uwp/host/AppxManifest.xml)

**cpu-times**. ARM uses port worker/thread/stack limits and default supportable graphics settings. Baseline reports actual CPU/wall intervals. Raw DXGI measures application Present cadence. Their timing domains differ.

**P0**. Collect matched medians and p95/p99 with CPU, wall and GPU kept separate. Standard mod route **no**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [memory.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/memory.md)

**perf-policies**. The official controls are carried where shared core paths permit them. UWP supplies its own worker/stack adaptations and ARM32 batching to fit memory. The baseline contains no cross-architecture setting sweep.

**P1**. Verify settings take effect at the required restart, and measure where constrained defaults help. Standard mod route **no**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

**benchmark**. The shell offers a standard benchmark and local report storage. The desktop report used ETW after PresentMon emitted no CSV. This does not verify all upstream profiling/replay or uploads from AppContainer.

**P1**. Test standard benchmark in a marked scratch world and collect reports per architecture. Standard mod route **conditional**. UWP host bridge **yes**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [README.md](../uwp/pregame/README.md) · [nightly-desktop-graphics-2026-11-20.md](../docs/nightly-desktop-graphics-2026-11-20.md) · [benchmark.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/benchmark.md) · [profiling.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/profiling.md)

**crashes**. Lumia fatal reporting recorded the prior shader binding error in LocalState and the log caught device loss. Some process exits produced no panic log. Backend probe failures should remain explicit.

**P1**. Verify clean diagnostics for failed probes, device loss and allocation errors. Standard mod route **conditional**. UWP host bridge **yes**.
[horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md) · [RENDERER_BACKENDS.md](../uwp/RENDERER_BACKENDS.md)

**telemetry**. The bridge enables heartbeat with the XAML shell and the consent UI has prior evidence. The design stores relevant reports locally. Remote delivery and current consent transitions have no blanket runtime verification.

**P2**. Verify local consent behavior and non-transmission paths with test transport. Standard mod route **no**. UWP host bridge **yes**.
[telemetry.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/telemetry.md) · [README.md](../uwp/pregame/README.md) · [Cargo.toml](../uwp/game-bridge/Cargo.toml) · [validation.md](../docs/validation.md) · [nightly-surface-2026-11-20.md](../docs/nightly-surface-2026-11-20.md)

**shader-cache**. Surfaces and compilation are functional in multiple signed runs. Lumia reports about 45 s first D3D11 pipeline creation. Wasm and resource staging may overlap. No exact target clock comparison exists.

**P1**. Compare cold/warm shader, guest and content sections without double-counting overlap. Standard mod route **no**. UWP host bridge **conditional**.
[graphics.md](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/graphics.md) · [horizon-uwp-0.1.12.patch](../patches/nightly-2026-11-18/horizon-uwp-0.1.12.patch) · [nightly-mobile-2026-11-20.md](../docs/nightly-mobile-2026-11-20.md) · [nightly-surface-2026-11-20.md](../docs/nightly-surface-2026-11-20.md) · [nightly-mod-runtime-2026-11-20.md](../docs/nightly-mod-runtime-2026-11-20.md)

## Device and build readiness

- **Lumia 950 XL** returned `Windows.Mobile` through the pinned Device Portal helper using Windows PowerShell 5.1. This checked reachability with the existing pairing. Angle measurements and physical touchscreen/controller checks still require someone to operate the device.
- **Surface Pro X** answered existing pinned-key SSH. `quser` reported active console session 1. The previous signed optional package required installation in that interactive session and registration with `-ExternalPackages`. [Surface installation evidence](nightly-surface-2026-11-20.md)
- **Ben-Desktop** is the build host. The first readiness snapshot showed ~73.4 GiB free on C:, ~1.43 GiB on D:, ~7.58 GiB on E: and ~148 MiB on F:. Use separate C: scratch and coordinate large Cargo targets. No deployment or device settings changed during these checks.

## Rechecking source coverage

- Compare all 1,327 paths from the complete local Git range `git diff --name-only --no-renames 0b14f8acfd97fa2c750e42795249dcc981178ca4 0a875da91dbb0fdcb7ba8c42c11341aa229af51a`. Read differences in renderer/input/host modules and manifests alongside the port patch. Published site docs can describe another release. [Pinned architecture](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/docs/architecture.md)
- Review the source-inventory section in the [JSON matrix](../deliverables/nightly-2026-11-20/feature-parity.json). Its `Graphics` and `PadSettings` entries map fields to the corresponding audited group. This includes internal state fields alongside settings. Each mod hook links to its declaration by repository-relative path. [Graphics source](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-render/src/graphics.rs) · [Controller source](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-input/src/gamepad.rs) · [Options UI](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-render/src/options_ui.rs) · [Renderer](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-client-render/src/gfx.rs) · [Mod API](https://github.com/horizon-rust/horizon/blob/0a875da91dbb0fdcb7ba8c42c11341aa229af51a/crates/horizon-mod-api/src/lib.rs)
