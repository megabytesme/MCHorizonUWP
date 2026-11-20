# Lumia and Surface follow-up

Lumia 1.0.0.5 rendered the full title and opened the existing saved
world. Both native and Wasm markers were visible, along with the
stock HUD, controller prompts and FPS overlay.
[Lumia title](../deliverables/nightly-2026-11-20/arm-hud/lumia-candidate/20261120_0007.png) · [Lumia saved world](../deliverables/nightly-2026-11-20/arm-hud/lumia-candidate/20261120_0008.png)

## Surface Direct3D 11

The game produced a populated title HUD batch and resolved its stock
sprites. Switching dynamic atlas writes and geometry writes from the
staging belt to queue uploads did not make the HUD visible. Both the
1× game screenshot and the 2× screenshot, which draws the same game frame
into an ordinary offscreen texture, omitted the HUD. This localized the
failure before XAML presentation.

On the Adreno 680, the no-depth HUD pass lost pixels if a preceding pass
had attached a depth view. The raw D3D11 reproduction confirmed that
`OMGetRenderTargets` returned no depth view in the second pass, but
the null D3D11 depth state still applied the previous pass's depth test.
Supplying an explicit disabled state restored all 12 expected samples
with devices created at FL11_1 and FL9_1.
[Failing wgpu replay](../deliverables/nightly-2026-11-20/arm-hud/evidence/surface-hud-depth-scope-replay.txt) · [Native FL9_1 control](../deliverables/nightly-2026-11-20/arm-hud/evidence/surface-hud-raw-depth-fl91.log)

The actual fork owns the fix on `codex/dx11-no-depth-state`. Commit
`aa7b65c43ef008d0a92d20fcdf9f10fa0883f157` creates the explicit
depth-disabled state in `wgpu-hal/src/dx11/hal_pipeline.rs`. It adds the
clear-depth to depthless-draw regression in `wgpu/tests/dx11_public.rs`.
Parent commit `5ce9beeaed132d00fc18ed80aa67bfb4e3508270` carries the
tracked upload-shadow optimization into the fork. The working `uwp`
branch retains its prior head `4c24e58de1ee12b9dd3a81cc4490662353011c30`
and existing user edits. Both new commits are local.

| Target | Verification |
| --- | --- |
| x64 | 555 HAL tests passed at physical FL11_1 and another 555 at forced FL9_1. Public depthless regression and mapped upload checks passed in both. UWP HAL type check passed. |
| x86 | Same two 555-test HAL runs and public checks passed in genuine i686 binaries. UWP HAL type check passed. |
| ARM64 | Fixed signed AppContainer replay passed 180 exact pixel checks on the Surface. Signed game 1.0.1.3 showed the stock title and both hooks on projected FL9_1 and actual FL9_1. A saved test world showed terrain, HUD, and both hooks on the projected path. |
| ARM32 | The updated fork passed the full strict bridge link and signature checks. Lumia 1.0.0.8 rendered the title and saved world on projected FL9_1. A separate signed AppContainer replay selected physical Adreno 430 FL9_1 and passed all 180 exact pixel comparisons and all nine queue/belt comparisons. |

The desktop HAL suites reported seven capability-dependent diagnostic
skips per run. The ARM64 test package changed the bridge while preserving
the previous host, assets and Wasm standard library. Its installed main
and optional identities both report 1.0.1.3.
[Fixed ARM64 replay](../deliverables/nightly-2026-11-20/arm-hud/evidence/surface-hud-depth-fixed-replay.txt) · [Surface title](../deliverables/nightly-2026-11-20/arm-hud/evidence/surface-fixed-candidate-20261120-211632/enabled.png) · [Surface actual FL9_1](../deliverables/nightly-2026-11-20/arm-hud/evidence/surface-fixed-fl91-candidate-20261120-212706/enabled.png) · [Surface world](../deliverables/nightly-2026-11-20/arm-hud/evidence/surface-world-candidate-20261120-215116/world.png)

The first Lumia probe blocked in the activation handler and recorded
50 correct pixels before the activation request timed out. Probe
1.0.0.4 ran the same linked Rust archive on the WinRT thread pool,
then flushed its completed report and exited. The replay tested
stock HUD shader draws with no preceding depth view, a prior clear at
zero, and a prior clear at 0.5. Its native FL9_1 adapter check rejects
a feature-level projection or software adapter.
[Lumia strict FL9_1 report](../deliverables/nightly-2026-11-20/arm-hud/evidence/lumia-strict-replay/wgpu-hud-replay-async.txt) · [Replay package and build sources](../deliverables/nightly-2026-11-20/arm-hud/evidence/lumia-strict-replay/)

MCHorizonUWP branch `codex/nightly-2026-11-18`, commit
`cf6a2905903c5f72fd92620ade2abb9da55eabb7`, adds Builder 1.0.6
patch 0.1.11 and client 0.1.12. Commit
`02744d00bda78d9ee7452b30b774340ec8df9ebe` adds patch 0.1.12
and client 0.1.13 to that Builder. Both pairs stage a Git archive of
`aa7b65c43ef008d0a92d20fcdf9f10fa0883f157` whose SHA-256 is
`8de8795116083533812728a855144a5f5e229d0562ef07990393bc5124c48c2e`.
The fork commit already contains the upload optimization. Earlier
Builder pairs use their original archive and sidecar patch. Both new
unsigned candidates assembled through the actual Builder. The source
assembly checks verified the archive checksum and matched the HAL,
upload shadow and regression sources against the fixed fork. The
Builder test suite also passed.
[Builder source selection](../uwp/builder/Core/SourceAssembler.cs) · [Nightly pins](../uwp/builder/Core/NightlyUwp.cs)

To export the source for `prepare_candidate.py --wgpu-archive` from
the local fork, run this on the build PC and verify its SHA-256 against
the Builder pin above.

```powershell
$revision = 'aa7b65c43ef008d0a92d20fcdf9f10fa0883f157'
$archive = Join-Path $env:TEMP "wgpu-$revision.zip"
git -C C:/dev/personal/wgpu archive --format=zip -9 "--prefix=wgpu-$revision/" "--output=$archive" $revision
Get-FileHash -Algorithm SHA256 -LiteralPath $archive
```

## Mobile gameplay changes

- The UWP touch controls were added to a HUD batch that the game
  discarded. The final batch now carries gameplay and inventory controls.
  The gyro settings loader reads the game directory where the launcher
  writes them. Gyro aiming remains opt-in.
- The existing controller radial picker includes Debug Screen. The
  updated client also offers **Toggle Debug Overlay** in Controller
  Settings → Bindings and a touch **F3** button beside Pause. Both call
  the debug visibility setter used by the keyboard route. The controller
  binding starts unassigned.
- The launcher and Horizon share `horizon-controller.txt`. The old
  serializer dropped `swapFaceButtons`, and the live game ignored it.
  The updated client writes and applies the same preference and updates
  button prompts. The switch waits for held face buttons to release.
  All 210 input tests and two headless round-trip and mapping tests passed.
- The displayed GPU time now says `n/a` until the backend supplies
  samples. The Lumia session logs `GPU timer off`. Its reported
  64 ms frame time corresponds to about 16 FPS. A 43 ms CPU timing plus
  a 64 ms wall interval does not identify the rest of the frame as GPU
  execution time.
- The build includes the existing device vibration module. It reacts to
  events such as local block breaks and damage. Motor output requires
  physical verification. The pinned Horizon tree also carries
  Starling-derived Rust touch layouts and drawing code.

These changes live in HorizonUWP patch 0.1.10 and client 0.1.11,
with the shared controller setting correction added in 0.1.11/0.1.12.
The F3 touch service also lives in
`uwp/touch-controls/src/lib.rs`, which is staged by the client archive.
The standalone touch and vibration tests passed. The ARM32 package build
uses `tools/build_nightly_runtime.ps1 -Architecture arm -AdditionalFeatures uwp-wasm`
with the pinned ARM32 Pulley dependency overrides and prebuilt host kit.

## Lumia startup and audio

The earlier isolated test bridge disabled audio by default. Lumia
1.0.0.7 enables it for the private device test. The first run hit a
graphics out-of-memory error during `Queue::write_texture` on the
renderer handoff. Its cold profile copied 23,219 prepared files totaling
248,353,373 bytes in 91.876 seconds. The signed test launch then hit
GPU allocation pressure while the UI atlas was being retained in memory.
[Startup error](../deliverables/nightly-2026-11-20/arm-hud/lumia-mobile/horizon-error-20261120-213529-914-3912-0.txt)

The retry used a 16 MiB block texture budget, which selected 32 px block
sprites instead of 64 px. The original isolated graphics file was
preserved. The swapchain still configured 2561×1439. The cached UI
bundle was ready in 9.242 seconds, while the game compiled 21–24 seconds
of Wasm work at launch and spent about 45 seconds creating the first
D3D11 pipelines. Some resource work overlaps pipeline creation.

The warm run reached the title and logged XAudio2 default output at
48 kHz stereo. The last logger entries show the Ogg serial for
`minecraft:music/menu/beginning_2`, a track that expands to about 61 MiB
of F32 PCM. The user reported a freeze and then an exit. This second
failure did not produce another Horizon panic record. A sound-effects
test disables the nine menu tracks and preserves the rest of the sound
settings. The isolated native and Wasm test fixtures remain installed.
[Warm title log](../deliverables/nightly-2026-11-20/arm-hud/lumia-mobile/warm-16mib-title-music.log)

With the nine title tracks excluded, the same signed build opened the
saved world and the user heard real sound effects. The screenshot
shows the touch HUD, debug information and `gpu n/a`. The user could
use touch buttons while movement and menu taps still needed work.
[Lumia in-world audio and touch test](../deliverables/nightly-2026-11-20/arm-hud/lumia-mobile/20261120_0010.png)

### Signed 1.0.0.8 update

Builder patch 0.1.12 contains the application fixes. The Rust bridge
passes the strict ARMNT AppContainer link against the Windows 10 build
15063 SDK and the package signature check. Device Portal confirmed
installation at 23:26:16 UTC on 20 November. The isolated profile
retains the 16 MiB texture budget and has its original render scale
and menu music settings.
[Signed Lumia package](../deliverables/nightly-2026-11-20/arm-hud/lumia-audio-touch/HorizonUWP-arm-1.0.0.8.signed.appx) · [Unsigned Builder candidate](../deliverables/nightly-2026-11-20/arm-hud/lumia-audio-touch/builder-unsigned/)

- `crates/horizon-client-render/src/audio.rs` and
  `crates/horizon-client-render/src/audio/stream.rs` decode streaming
  tracks on a worker and bound the PCM queue to about 480 KiB at
  48 kHz stereo. Short sound effects retain the static path. Six
  focused tests passed, including sample-for-sample comparison with
  the old decoder on the exact menu track and tests for mixing, pause
  and end-of-stream. The focused ARM32 audio type check passed.
- `crates/horizon-client-render/src/gfx/fl91.rs` retires the startup
  tile upload staging buffers one batch at a time on ARM32 UWP. The
  dynamic tile upload path retains its existing scheduling.
- `crates/horizon-client-render/src/game/network/remote/mod.rs` reads
  the shared movement state, so the touch joystick contributes to
  movement. `uwp/touch-controls/src/lib.rs` accepts the full square
  shown on screen. Meaningful gamepad input hides the touch HUD and a
  new gameplay touch restores it. Four touch service tests and the
  input handoff harness passed.
- `crates/horizon-client-render/src/game.rs` and the UWP files under
  `third_party/winit` route an unclaimed primary touch through the
  usual menu cursor and button events. Touches consumed by gameplay
  controls do not also generate menu clicks. A headless harness
  exercised the actual Winit event route.

The user confirmed audible title music, working menu taps and working
MOVE controls on 1.0.0.8. The log records non-silent XAudio2 output
from the streaming run and then starts the integrated server for the
saved world. This launch spent 20 seconds compiling the Wasm fixture,
45 seconds creating D3D11 pipelines and about 104 seconds staging its
248 MB of mod content. Content staging and pipeline creation overlapped.
[1.0.0.8 game log](../deliverables/nightly-2026-11-20/arm-hud/lumia-audio-touch/1.0.0.8-32-chunks.log)

The same session used a 32-chunk render distance. During play, D3D11
reported `E_OUTOFMEMORY`, terrain allocations failed, and the graphics
device was lost. The application arena uses growable copy-source
buffers. The HAL allocates a CPU shadow for those buffers, and both
shadow allocation and native buffer creation can produce the same
HRESULT. The log does not identify which allocation failed. The test
profile was backed up and its Render Distance set to 2 chunks. The
user reported stable play in the same saved world during the short
retry and confirmed that GameSir input hid the touch HUD and touch
restored it. This establishes a stable short run with the lower
distance. A sustained frame-rate comparison and the exact failing
allocation at 32 chunks remain unverified. The test profile retains
2 chunks and the pre-test file remains backed up.

At 50% Resolution Scale, the Lumia still reported a 2561×1439 swapchain.
The FL9_1 compatibility path skips the scaled world post-process and
disables FSR 3.1 frame generation. The scale displayed by the debug
overlay is the configured value. The measured effect of lowering the
texture budget was a reduction in block sprite resolution from 64 to
32 px. A change to the swapchain resolution would be separate work.
