# November 20 Nightly runtime follow-up

Initial product worktree `codex/nightly-2026-11-20` started at `15625f842f81b13cc29e4829c73ffa5d8d419e79`. The Horizon source pin remains `0a875da91dbb0fdcb7ba8c42c11341aa229af51a`. D3D11 still uses wgpu `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157`. The November 18 product baseline at `2e98d4555313e2d9485c207ddc1962e2636001bd` and the dedicated wgpu worktree were clean. The separate wgpu checkout retained its two previous unstaged modifications. [Prior candidate, signatures and source checks](nightly-integration-2026-11-20.md)

## Milestones

| Lane | Scope | First new evidence |
| --- | --- | --- |
| Lumia performance and DRS | Device access, signed saved world and matched scale runs | The paired Device Portal responded. The signed isolated DrsSmoke package bootstrapped the copied two-chunk save. The user heard world audio while the pre-title launcher spinner stayed visible. |
| Gyroscope aiming and controls | Sensor path, motion evidence and input checks | Compiled the host report-order and orientation-generation fix on all four architectures. Four focused Rust tests passed against the pinned mod API. The optional mod trace records before/after yaw and pitch once enabled. |
| Surface Pro X | Signed D3D11 and D3D12 interactive play | D3D11 at 67% rendered a saved world and both mod markers. The first D3D12 capture showed real world geometry under the terrain-loading screen. It was captured before both markers appeared. |
| Desktop graphics, mods and multiplayer | Stalled startup, repeated launches, hooks and dedicated-server join | Suspended thread snapshots put the render worker in the XAML swapchain-attachment handoff and the XAML owner in a synchronous SecondaryTile activation call. |
| Builder and release checks | Immutable source and local candidate | Builder 1.0.8 accepts the new patch 0.1.14/client 0.1.16 recipe. The 0.1.15 diagnostic client uses patch 0.1.13. Clean source assembly, locked Cargo metadata and all four catalog compatibility entries passed. |

## Surface gameplay

The existing signed `MCHorizonUWP.NightlyNov20.arm64` 1.0.1.0 package and its related native-mod bundle ran on Ben-Tablet in console session 1. Hardware D3D11 reported the Qualcomm Adreno 680, with the game using the FL9_1 compatibility path on FL11_1 hardware. At fixed 67%, the world target was 1372 × 1029 and the output was 2048 × 1536. The saved world presented 9,216 exact cyan and 6,432 exact magenta pixels in the sampled HUD region. F3 rendered and the test exited cleanly. This run had audio disabled. [Signed run and graphics receipt](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/dx11-world-67/result.json) · [World and HUD](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/dx11-world-67/world.png) · [F3 capture](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/dx11-world-67/f3.png)

The second hardware D3D12 run waited for the world's terrain-loading screen to clear. It reached 2048 × 1536 on the physical Adreno 680, with both markers, keyboard movement from Z 58.500 to 64.109, creative inventory, chat, and non-silent XAudio2. The app regained its original geometry and both markers after minimize and restore. A second hardware D3D11 run at 67% verified audio and the same game controls. Deliberately resizing moved the world/output from 1372 × 1029 / 2048 × 1536 to 1025 × 758 / 1530 × 1132 and back, with both hooks visible. Chat also reduced available output height and then restored it. Both runs exited cleanly. These are signed client 0.1.14 results. [D3D12 result](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/dx12-world-100/result.json) · [D3D12 captures](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/dx12-world-100/contact-sheet.jpg) · [D3D11 result](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/dx11-world-67-interactive/result.json)

The initial hardware D3D12 adaptive check used a 67% floor and 144 FPS target. Verified game frames remained at 100% after resize and restore, while F3 reported 619,135,574,736,896 ms across three GPU passes. That measurement is invalid. The interval recorded 21.84 app CPU seconds over 40.78 wall seconds and a 1,399,955,456-byte peak working set. [Adaptive control capture](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/dx12-adaptive-timestamp-control/adaptive-f3-scale.png)

## Desktop graphics startup

The captured x64 game worker had already obtained a D3D11 device. It blocked in `horizon_uwp_dispatch_ui_sync` inside the SwapChainPanel attachment. The XAML owner simultaneously blocked in `RoGetActivationFactory`. The executable call referenced `Windows.UI.StartScreen.SecondaryTile`. Existing saved-server entries triggered that synchronous call when their status completed. In a control that temporarily moved only the digest-verified test `servers.dat` fixture, the same signed baseline reached an integrated world and logged a first frame. The harness restored the fixture. [Debugger excerpt](../deliverables/nightly-2026-11-20/runtime-evidence/x64/watchdog-secondary-tile/stack-excerpt.txt)

The fix moves background server and world tile status work off XAML's dispatcher. Explicit user actions still own their pin and unpin dialogs. In the signed host-fix run with the saved-server fixture present, the attachment reached the XAML callback in 19 ms and completed `SetSwapChain` in 70 ms from the initial request. The Rust renderer acquired, submitted, and presented its first loading frame. The signed x64 warm launch reached active gameplay in 40.369 seconds. Signed x86 cold and warm launches reached it in 59.422 and 17.260 seconds respectively. Each used the original saved-server trigger and drew both hooks. The x64 first cold run reached the swapchain and was closed by the harness during a 248 MB resource copy. Both x86 trials and the x64 warm trial closed cleanly. [x64 warm](../deliverables/nightly-2026-11-20/runtime-evidence/x64/host-fix-warm-world/result.json) · [x86 cold](../deliverables/nightly-2026-11-20/runtime-evidence/x86/host-fix-cold-world/result.json) · [x86 warm](../deliverables/nightly-2026-11-20/runtime-evidence/x86/host-fix-warm-world/result.json)

## Desktop multiplayer

Signed x64 and x86 D3D11 clients each logged in to the exact pinned 26.3 dedicated server, rendered both mod markers, moved and disconnected. Each was then launched in a fresh process and the stock server logged the player's persisted movement position. x64 moved from `(-527.500, 69.000, 64.500)` to `(-527.610, 66.000, 70.527)`. x86 resumed at `(-527.500, 66.000, 70.712)`. The x64 same-host wired-address test reached the connector and timed out. The successful offline allowlisted test used a temporary loopback exemption for each exact isolated family. The listener stopped, both worlds saved and both exemptions were removed. [x64 login and cleanup](../deliverables/nightly-2026-11-20/runtime-evidence/x64/host-fix-multiplayer/) · [x64 reconnect](../deliverables/nightly-2026-11-20/runtime-evidence/x64/host-fix-multiplayer-reconnect/result.json) · [x86 login and cleanup](../deliverables/nightly-2026-11-20/runtime-evidence/x86/host-fix-multiplayer/) · [x86 reconnect](../deliverables/nightly-2026-11-20/runtime-evidence/x86/host-fix-multiplayer-reconnect/result.json)

## Signed mod restart and admission

| x64 D3D11 state across fresh processes | Native marker pixels | Wasm marker pixels |
| --- | ---: | ---: |
| Both enabled | 4096 | 4096 |
| Native disabled | 0 | 4096 |
| Wasm disabled | 4096 | 0 |
| Both re-enabled | 4096 | 4096 |
| Both disabled | 0 | 0 |

Every capture showed actual saved-world play. The installed native and Wasm package files remained present for each test. The logs agreed with the enabled inventory. A separate signed package carrying an altered expected native SHA-256 was refused before its DLL could load. Both active hooks also rendered on signed x64 hardware D3D12 and ANGLE. The signed Surface candidate ran native-only on D3D11 (9,216/0) and Wasm-only on D3D12 (0/6,432) after clean process exits. [Desktop matrix](../deliverables/nightly-2026-11-20/runtime-evidence/x64/mod-restarts/matrix.json) · [Rejected native manifest](../deliverables/nightly-2026-11-20/runtime-evidence/x64/bad-native-checksum/result.json) · [Surface D3D11 control](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/dx11-native-only/result.json) · [Surface D3D12 control](../deliverables/nightly-2026-11-20/runtime-evidence/arm64/dx12-wasm-only/result.json)

## Process lifetime and mod settings

At the pinned upstream revision, the engine seal, point table and platform provider are process globals. The old XAML return-to-home flow cleared a host flag and queued another game within the same process. The second engine startup would try to install its already-installed registration provider. The host now completes view and input cleanup, holds its launch guard, and requests a fresh launcher process on Windows versions that support the restart API. The legacy ARM32 route closes after Quit Game so the next activation starts with a new process. Both retain the ordinary in-game return-to-title behavior until the player quits the game. The modern route falls back to closing if Windows rejects the restart. [WinRT restart API](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.core.coreapplication.requestrestartasync?view=winrt-19041)

The scoped quit lifecycle change strict-linked in x64 and ARM32 host builds before integration with the remaining runtime changes. Physical restart verification uses the signed package and records both process identities.

## Runtime source fixes and reproducible candidate

The renderer's world handoff now releases the previous FL9_1 tiled HUD atlas after the existing queue wait and before uploading its replacement. A 2048 × 2048 RGBA atlas tile consumes 16 MiB. The original path released the single-texture atlas, which the FL9_1 tiled path does not allocate. The recorded Lumia run demonstrated an out-of-memory error during a world texture upload at 16 MiB block textures and later reached the world using 4 MiB with a warm cache. The new patch addresses the overlapping startup allocation. That change needs a separate physical acceptance run.

The UWP screenshot publisher moves an already encoded temporary file into the destination on the same volume with replacement disabled. A collision test retained the original destination bytes and kept the staged file available for cleanup. An unclaimed destination received the complete bytes and removed the temporary path. [Windows collision result](../deliverables/nightly-2026-11-20/runtime-0.1.16/source-checks/screenshot-move-contract.json)

GPU timing now rejects zero-duration whole frames, reversed or invalid timestamp pairs and impossible spans. The UWP renderer uses measured frame pressure after three consecutive rejected whole-frame readbacks, returns to GPU timing after three valid readbacks, and resets the old measurement window on source change. Missing query results and per-pass-only faults do not change DRS timing source. The 20 isolated timing and controller tests passed. The Adreno 680 needs a follow-up run of the corrected binaries. [Timing tests](../deliverables/nightly-2026-11-20/runtime-0.1.16/source-checks/timestamp-fallback-tests.log)

The local development export used product commit `726ef25154dd9ef2998ea725f02dea83157ef9d8` and the immutable Horizon ZIP with SHA-256 `abd6a91c41407522f9b0ec43d16555f46cd76832625645658bee57e2ab49da3d`. The patched source assembled in a new directory. All four unsigned catalog compatibility rows accepted patch 0.1.14, client 0.1.16 and Builder 1.0.8. The clean source resolved Cargo metadata with the locked offline inputs. The signed public catalog stays separate. [Builder files and hashes](../deliverables/nightly-2026-11-20/runtime-0.1.16/builder-candidate/) · [Immutable tool inputs](../deliverables/nightly-2026-11-20/runtime-0.1.16/source-checks/immutable-inputs.sha256)

```powershell
& ./tools/build_host_artifact_kit.ps1 -OutputDirectory $HostKit
python tools/prepare_candidate.py --revision 726ef25154dd9ef2998ea725f02dea83157ef9d8 `
  --patch-version 0.1.14 --client-version 0.1.16 --builder-version 1.0.8 `
  --upstream-repo $HorizonGit --upstream-archive $HorizonZip --wgpu-archive $WgpuZip `
  --host-artifacts $HostKit --wasmtime-archive $WasmtimeCrate --wasm-standard-library $WasmStandardLibrary `
  --output $Candidate
dotnet run --project uwp/builder/Tests/Horizon.Builder.Tests.csproj -c Release
dotnet run --project uwp/builder/Tests/Horizon.Builder.Tests.csproj -c Release --no-build -- `
  --assemble-candidate $HorizonZip "$Candidate/horizon-uwp-0.1.14.patch" `
  "$Candidate/uwp-client-0.1.16.zip" $CleanAssembly 0a875da91dbb0fdcb7ba8c42c11341aa229af51a 0.1.16
```

Use the existing locked release build and strict package scripts with `-PatchVersion 0.1.14 -ClientVersion 0.1.16 -UpstreamCommit 0a875da91dbb0fdcb7ba8c42c11341aa229af51a`. Optional native packages are staged from trusted sources using each architecture's matching build identity. ARM32 embeds those sources and uses the legacy Wasmtime link profile. [Build and staging procedure](nightly-cross-architecture-2026-11-20.md)
