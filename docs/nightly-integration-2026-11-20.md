# HorizonUWP November 20 Nightly integration

The November 20 work lives in the separate `codex/nightly-2026-11-20` product worktree. The November 18 product branch, the wgpu fix worktree, and the uncommitted changes in the other wgpu checkout were preserved.

## Verified November 18 baseline

Both worktrees had zero staged, unstaged, or untracked files at inspection and again after publication. The game had used patch 0.1.12, client 0.1.13, Builder 1.0.6, and the Lumia's isolated 1.0.0.8 APPX. [Prior ARM and mobile runtime evidence](nightly-mobile-2026-11-20.md)

### MCHorizonUWP

Branch `codex/nightly-2026-11-18`, HEAD `2e98d4555313e2d9485c207ddc1962e2636001bd`.

| Commit | Subject |
| --- | --- |
| `2e98d4555313e2d9485c207ddc1962e2636001bd` | Record verified ARM HUD and Lumia gameplay repairs |
| `02744d00bda78d9ee7452b30b774340ec8df9ebe` | Stream music and route Lumia touch through gameplay and menus |
| `cf6a2905903c5f72fd92620ade2abb9da55eabb7` | Restore mobile controls and pin the repaired D3D11 fork |
| `e5e53c2d136dd922c633af09ca8bdcb15d5c3a08` | Document matched Nightly desktop graphics captures |
| `fabe9206dd4358f1a503934e7c956f3c8e3d96cf` | Record signed Nightly mod tests and ARM HUD investigation |

The earlier `f6fda2c7e` commit added native and Wasm runtime integration. `fc1418389` corrected the FL9_1 stars uniform and font fallback, and `df59bd030` kept resource discovery and scaled HUD fixes aligned.

### wgpu-dx11

Branch `codex/dx11-no-depth-state`, HEAD `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157`.

| Commit | Subject |
| --- | --- |
| `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157` | dx11: explicitly disable depth on depthless render pipelines |
| `5ce9beeaed132d00fc18ed80aa67bfb4e3508270` | dx11: reuse the upload buffer CPU shadow |
| `4c24e58de1ee12b9dd3a81cc4490662353011c30` | dx11: unlock hardware feature levels and remove draw readback stalls |
| `6007bfc682767b04ca742152d5f9d0feadd6602b` | hal: use undecorated raw-dylib import name for LoadLibraryExW on x86 |
| `5c4601136bc141e50a580a6e367fa1e908534f04` | dx11: add UWP SwapChainPanel surface and composition swapchain support |

The separate `wgpu` checkout was already dirty on `uwp` at `4c24e58de1ee12b9dd3a81cc4490662353011c30`. Its two unstaged edits were `wgpu-hal/src/dx11/adapter.rs` and `wgpu-hal/src/dx11/buffer.rs`. The existing buffer change matches the committed upload-shadow change. The large Git differences between product `main` and the November 18 branch were tracked previous-mission history.

## Publication

| Repository | Verified remote head | Integration |
| --- | --- | --- |
| [MCHorizonUWP](https://github.com/megabytesme/MCHorizonUWP) | `codex/nightly-2026-11-18` at `2e98d4555313e2d9485c207ddc1962e2636001bd` | [PR #1 to main](https://github.com/megabytesme/MCHorizonUWP/pull/1). Remote main stayed at `f67b82e354e3ccd098a4d31e8bcdbe35f8afcffe`. |
| [wgpu-dx11](https://github.com/megabytesme/wgpu-dx11) | `codex/dx11-no-depth-state` at `aa7b65c43ef008d0a92d20fcdf9f10fa0883f157` | [PR #1 to uwp](https://github.com/megabytesme/wgpu-dx11/pull/1). Remote uwp stayed at `4c24e58de1ee12b9dd3a81cc4490662353011c30`. The default is `uwp-dx11` at `440895454850d54435c5bca3e99bab36e57f8137`. There is no remote main. |

The product branch is a descendant of remote main. A direct update would replace the live signed catalog and its signature with the version from the earlier Builder 1.0.3 changes. The signed catalog in the published branch has no public recipe for Builder 1.0.6. The November 18 isolated Builder tests passed. In the wgpu fork, `uwp` is the direct base of the two renderer fixes. The fork's default branch is the earlier FL9_1 backend history.

MCHorizonWiiU and other ports can reuse the exact Nightly SDK and manifest validation, typed hook registration, Wasm/Pulley integration and ABI fixtures, native source Builder design, and incremental resource staging with generation retention. The UWP allocator and linker choices inside Pulley need platform-specific adaptation. WinRT input, XAudio2 streaming output, Windows optional packages, APPX signing, strict AppContainer linking and the D3D11 renderer are Windows-specific. [Published mod runtime report](https://github.com/megabytesme/MCHorizonUWP/blob/2e98d4555313e2d9485c207ddc1962e2636001bd/docs/nightly-mod-runtime-2026-11-20.md)

## November 20 source

The pinned official source is `0a875da91dbb0fdcb7ba8c42c11341aa229af51a` under `nightly-source/2026-11-20-0a875da`, Git tree `84463ab1b850d9ac382fb0b94d247f0c06d690fc`. Both Horizon Ed25519 checksum signatures verified against the upstream release key. The retained release archive SHA-256 is `921fc63efde1d8c226ba6c0457677b41effd7be367ba93116ccb424a9790eb75`. All 11,596 archive blobs matched Git. The upstream range has 331 commits and 1,327 paths with renames expanded. With rename detection, Git reported 1,310 files changed, 87,419 insertions and 19,560 deletions. SDK Rust is 1.99.1, resource schema is 7, and preparer output version is 5. [Official release](https://github.com/horizon-rust/horizon/releases/tag/nightly) · [Pinned source](https://github.com/horizon-rust/horizon/tree/nightly-source/2026-11-20-0a875da)

## Milestones

| Owner | Work | Evidence |
| --- | --- | --- |
| Integration | Semantic migration | Tracked November 18 patch applied with a three-way merge. Resolved renderer, input, staging, allocator and guest-stack conflicts against the November 20 implementations. |
| Renderer | FL9_1 world scaling and fallback timing | 32 desktop offscreen probe cases and eight signed hardware cases on each of Surface Pro X and Lumia 950 XL passed. Lumia gameplay and adaptive performance remain open. |
| Native input | Gyroscope aiming mod | Standard typed-hook mod, focused tests and four strict-linked UWP host kits. Surface WinRT reports an available physical gyrometer. In-world motion verification remains open. |
| Audit and QA | Feature matrix | [108 feature groups across 11 profiles](horizon-uwp-feature-parity-2026-11-20.md). November 18 runtime observations are identified separately from November 20 source findings. |
