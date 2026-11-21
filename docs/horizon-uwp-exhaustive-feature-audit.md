# Horizon Windows and UWP settings audit

The comparison uses official Horizon Windows `0a875da91dbb0fdcb7ba8c42c11341aa229af51a`, the November 20 MCHorizonUWP product at `5c26a8060c1cf2dce20ee5707e84699ed0879e5c` and the wgpu fork at `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157`. The product uses Builder 1.0.9, engine patch 0.1.15 and bridge client 0.1.17. Signed runtime receipts identify their own package and bridge versions. Earlier receipts qualify only the device, version and exercised behavior they name. The baseline report groups 108 features and includes November 18 evidence. [Historical parity](horizon-uwp-feature-parity-2026-11-20.md) · [Signed November 21 follow-up](nightly-runtime-completion-2026-11-21.md)

## Inventory and how to read it

The extract records 1,062 settings and actions, 108 feature groups and 16 UWP profiles. Coverage indexes 175 file entries across official source, patched source and product evidence. The renderer matrix compares 42 behaviors, and the shader matrix records 46 cases.

| Artifact | Contents |
| --- | --- |
| [Settings inventory](../deliverables/feature-audit/settings-inventory.json) | Source IDs, storage, aliases, source lines, official Windows behavior, UWP UI and runtime behavior, enabling conditions, defaults, value domains, test methods and profile-specific statuses and evidence |
| [Settings matrix](../deliverables/feature-audit/settings-matrix.csv) | Filterable settings and feature rows with profile status columns |
| [Coverage](../deliverables/feature-audit/coverage.json) | Per-file declarations and counts, explicit metadata and scope gaps |
| [Unsupported features and backlog](../deliverables/feature-audit/unsupported-features.md) | Proven gates, inactive paths, missing behavior and scoped failures, with next acceptance evidence and owning layer |
| [Renderer comparison](horizon-uwp-renderer-comparison.md) | D3D12, D3D11 and ANGLE against official Windows, including actual FL, compatible projection and architecture |
| [Shader compatibility](horizon-uwp-shader-compatibility.md) | Native Shader API and classic pack stage, interaction and physical evidence matrices |

| Status | Meaning |
| --- | --- |
| **V** | Functionally verified within the specific test scope |
| **P** | Partial implementation or evidence |
| **B** | Observed failure on the stated configuration |
| **S** | Stub or configuration with no effective action |
| **H** | Intentionally hidden or gated |
| **U** | Missing port implementation |
| **X** | Demonstrated OS or hardware lack within the stated scope |
| **C** | Source only or runtime proof pending |
| **?** | Unknown |

An official Windows cell is independent of its UWP cells. A `V` for one point on a slider applies to the tested values. A successful game open, adapter capability, shader compilation and visual effect each have separate evidence. `null` marks absent or unresolved metadata. Coverage lists the source files and declaration identifiers checked. Third-party packs and mod pages contribute their own runtime values. See the remaining scope and metadata entries before using the inventory as a completeness gate.

## Graphics findings

| Setting or route | Official Windows and UWP comparison | Evidence or next proof |
| --- | --- | --- |
| Graphics API | Official preferences offer Auto, Vulkan, DX12 and OpenGL on Windows. AppContainer selects the packaged backend through the XAML host. Signed x64 gameplay reached D3D12 and ANGLE as well as the separate D3D11 route. x86 D3D12 presented a launcher probe frame. Its ANGLE probe failed at surface creation. | [Renderer comparison](horizon-uwp-renderer-comparison.md) |
| D3D11 negotiation | Final x64/x86 gameplay, tested Surface and Lumia runs report FL11_1 while the game selects its FL9_1 compatibility renderer. The separate signed and native forced-FL9_1 scaler probes exercised color, depth and HUD readbacks. | Each tier needs its own full native/HAL and in-game certification. [DRS probe report](nightly-drs-2026-11-20.md) |
| Presets and vanilla controls | The extractor records every Horizon preset table and common assignment, alongside the individual linked settings. Preset application can set values whose effects depend on the current backend, including FXAA and GPU culling. | Establish both persisted settings and fixed-camera output per preset. |
| Fixed world scale | Signed D3D11 runs verified internal color/depth dimensions and output-size HUD on desktop and mobile at the recorded steps. | x64 50%, x86 75%, Surface 67% and Lumia 50% have distinct receipts. [Signed runtime](nightly-runtime-completion-2026-11-21.md) · [Lumia run](nightly-lumia-drs-2026-11-21.md) |
| Dynamic resolution and target FPS | Compatibility path can use frame pressure. Lumia 0.1.16 changed to 55% with a 50% floor and a 30 FPS target. Surface D3D12 0.1.14 reported invalid GPU timings and stayed at 100%. The later UWP timestamp guard has source and unit-test evidence. The tested ANGLE path lacks both a timer and the compatibility no-timer fallback. | Repeat sustained matched scenes, recording dimensions, scale changes, timing source and actual delivery cadence. [Timing follow-up](nightly-runtime-completion-2026-11-21.md#surface-gameplay) · [Lumia trajectory](nightly-lumia-drs-2026-11-21.md) |
| FXAA, TAA, TAA upscaling and sharpness | The D3D11 compatibility setter applies scale and bypasses the modern post parameters. The modern source path has format, history and pack constraints. | Run edge/motion A/B with selected AA and controlled scene targets. [Renderer matrix](../deliverables/feature-audit/renderer-features.csv) |
| 4× MSAA | The installed renderer rejects the route on D3D11 and GLES. DX12 checks its terrain format and resolve capabilities. | Capture actual four-sample allocation and resolve on the eligible DX12 route. [Renderer matrix](../deliverables/feature-audit/renderer-features.csv) |
| FSR 3.1 frame generation | The compatibility renderer forces it off. ANGLE lacks the surface copy destination required to present an interpolated frame. DX12 retains the compute/interpolation path and its frame gates. | On DX12 measure generated-frame cadence and visual output with compatible HUD, then repeat interacting settings and resize. [Renderer matrix](../deliverables/feature-audit/renderer-features.csv) |
| HDR Auto | Stock Windows DX12 presented HDR10 PQ in Auto with system HDR active. UWP DX12 exposes PQ/scRGB capability, and its SwapChainPanel initializes without the display-state provider that Auto consumes. D3D11 and GLES have independent frame/surface restrictions. | Record OS HDR state, effective game request and selected DXGI format and color space. [Official Auto receipt](../deliverables/feature-audit/official-windows-hdr-receipt.json) · [Renderer comparison](horizon-uwp-renderer-comparison.md) |
| HDR On, calibration and look | Manual On needs physical confirmation on the actual UWP compositor path. Black Level, Paper White, Peak Brightness, UI Brightness, Color Boost and Look are separately listed. Pack-controlled lighting, the HUD layer and the compositor can disable individual controls. | Verify actual nits and black, white and peak test patterns, transitions and fallback. [Renderer comparison](horizon-uwp-renderer-comparison.md) |
| GPU draw/culling features | Counted indirect terrain, translucent batching, occlusion, mesh shaders and ray queries have separate predicates. The flat-page DX12 GPU-culling path has a base-vertex limitation. | Acquire engine dispatch and visible output evidence for each admitted path. [Renderer matrix](../deliverables/feature-audit/renderer-features.csv) |
| Native and classic shader packs | The signed x64 SDR console sequence captured isolated D3D12 sky, material, single and ordered post, and classic composite. D3D11 refused the native marker and hit a pre-gate terrain copy validation error on the classic fixture. ANGLE refused the native layouts and depth-load translation. The classic hand and entity layouts required missing vertex storage. Broader stage variants, options and interactions remain open. | [Stage and fixture results](horizon-uwp-shader-compatibility.md) |

## Gameplay, settings and platform

| Area | Verified or source-backed scope | Acceptance gap |
| --- | --- | --- |
| Saved world and restart | Signed desktop builds entered a saved world. The dedicated x64 restart receipt captured save and quit, a new launcher process, the same world and both GUI markers. | Creation modes and world rules, damaged file handling, suspend/resume, and broader device/architecture restart loops. |
| Multiplayer and auth | Signed x64/x86 D3D11 joined a pinned offline dedicated server, moved and later rejoined at the stored location. One x64 test matched client/server content registries. | Account refresh and online-mode multiplayer, LAN discovery, Realms and ordinary remote-host networking. The bridge omits the Play with Friends optional tunnel. |
| Native, Wasm and content mods | Signed x64 independent toggles and re-enable each affected their own GUI marker. Modern UWP uses signed related sets for native mods. Lumia embeds trusted source at build time. The Wasm fixture used Pulley on AppContainer. | Full API and mod combinations, install/update and damaged package handling require separate evidence. |
| Audio and language | Signed XAudio2 produced audible/non-silent saved-world output on recorded routes. UWP uses default output. The packaged path omits desktop CPAL endpoint selection. | Exercise source/category volume, music scheduling, positional mix, language, subtitles and device switching in individual trials. |
| Accessibility and input | Shared accessibility settings persist. The UWP speech backend is inactive. Signed desktop and Surface played with keyboard movement, and Lumia physical motion controlled both yaw and pitch in a documented session. | Narration, assistive navigation, IME and clipboard, touch/controller coexistence, rumble and both gyro orientations. |
| Capture and storage | Signed 0.1.17 produced game-generated PNGs. The Lumia mod-triggered screenshot succeeded at 2561×1439 after an error-5 path fix. | Verify actual F2 and quality settings, collision behavior on target devices and ownership of exposed platform folders. |
| Window mode and lifecycle | UWP's Winit fullscreen setter and exclusive-mode enumeration do not implement the desktop mode controls. XAML manages the hosted presentation independently. | Device-specific shell fullscreen, application suspension and restoration, and input focus under resize and monitor change. |

These are feature-level receipts. The individual option rows retain their own statuses. [Runtime follow-up](nightly-runtime-completion-2026-11-21.md) · [Lumia storage, DRS and gyro evidence](nightly-lumia-drs-2026-11-21.md) · [Native installation](native-mods-uwp-2026-11-19.md)

## Unverified and defective behavior

The follow-up ledger gives each unresolved setting, feature, renderer and shader cell its own row with the backend, architecture, D3D11 feature level when applicable, observed blocker, evidence, reproduction and repair owner. Filter `classification` for missing implementation, stub, broken, driver or platform limits, intentional gating, untested behavior, or official upstream limitations. A partial result retains a test for its open portion. The coverage test compares the ledger to every non-verified cell in all four matrices. [Verification ledger](../deliverables/feature-audit/verification-gaps.csv) · [Coverage counts](../deliverables/feature-audit/coverage.json)

| Owner | Pinned operation | Reproduction and completion evidence |
| --- | --- | --- |
| HDR | UWP D3D12 Auto has no display-state provider at the product pin. The separate HDR branch has signed x64 scRGB evidence. | Check OS HDR state, selected color space, presenter metadata and distinct HUD levels on the same scene. Repeat Auto toggle, transition and non-HDR fallback on the exact candidate. [Renderer comparison](horizon-uwp-renderer-comparison.md) |
| D3D11 tier owner and UWP engine | Signed x64 FL9_1 compatibility on hardware L11_1 refused the native fixture. Classic atlas preparation failed before its configured refusal. | Gate classic selection before GPU preparation. Recheck a stock frame after refusal on the x64 compatibility route and on physical FL9_1. Test the modern path on a candidate that exposes it. [Shader comparison](horizon-uwp-shader-compatibility.md) |
| ANGLE and Horizon shader engine | Signed x64 GLES 3.0 isolated native sky and material layout refusal, post depth-load translation, and classic hand and entity vertex-storage failure. Stock post-effect alignment is independently reproducible. | Use the console controls to verify layout specialization, GLSL-compatible depth input and conditional classic geometry allocation. Require the expected markers and a playable fallback. [Shader fixtures](../tools/feature-audit-fixtures/README.md) |
| UWP platform and QA | Narrator backend is a stub and UWP audio routing uses the default endpoint. Earlier signed touch, controller, gyro and reconnect evidence covers specific profiles and package versions. | Record the selected setting and actual speech or audio routing, controller/touch handoff, copied clipboard text, observed focus and lifecycle events, and a non-loopback join against a dedicated test server. [Verification ledger](../deliverables/feature-audit/verification-gaps.csv) |

## Remaining physical scope

The signed x64 console run exercised 17 staged cases across D3D12, D3D11 and ANGLE. D3D12 and D3D11 produced in-series stock controls. Both ANGLE pack-off attempts timed out, so the earlier exact-package ANGLE control remains the reference. Surface session 1 remained locked at the foreground check in the prior mobile pass. The Lumia Device Portal returned HTTP 503 and timed out on retry. The retained mobile receipts cover the earlier tests, and the newly isolated cases remain open on those devices. [Shader runner and receipts](horizon-uwp-shader-compatibility.md) · [Device-specific proof gaps](horizon-uwp-renderer-comparison.md)

## Reproduce the extract

Use Python 3.11 or later. Check out the official SHA in a clean directory. Apply the exact product patch to a separate detached checkout at that SHA. Run these commands in the audit worktree with both source paths supplied. Set `$MinecraftEnUs` to `assets/minecraft/lang/en_us.json` from the prepared 26.3 resources. The checked inventory records the language file SHA-256 and retains each resolved translation key.

```powershell
git -C $UwpPatchedSource apply --check "$PWD/patches/nightly-2026-11-20/horizon-uwp-0.1.15.patch"
git -C $UwpPatchedSource apply "$PWD/patches/nightly-2026-11-20/horizon-uwp-0.1.15.patch"
python -B tools/audit_horizon_settings.py --official $HorizonOfficial --uwp-source $UwpPatchedSource --minecraft-language $MinecraftEnUs --check
$env:HORIZON_AUDIT_OFFICIAL = $HorizonOfficial
$env:HORIZON_AUDIT_UWP_SOURCE = $UwpPatchedSource
python -B -m unittest discover -s tools -p test_audit_horizon_settings.py -v
```

Omit `--check` to regenerate the five extractor outputs. The tests independently compare source match arms, serializer entries, static controls, bundled manifests and profile evidence requirements. The shader and renderer matrices carry their separate stage and hardware receipts.
