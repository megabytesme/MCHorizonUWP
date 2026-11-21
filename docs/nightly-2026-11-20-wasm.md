# November 20 Wasm ABI verification

The stock Windows x64 Nightly at 0a875da91dbb0fdcb7ba8c42c11341aa229af51a loaded a fresh standard GUI Wasm mod using a regenerated shared standard library. The enabled run rendered the real hook in a new creative world. The disabled control reached the title in a separate clean profile. A separate title run confirmed the trap stopped its own group and the visual fixture kept drawing. All three runs exited with code 0. This is the desktop reference for the separate Pulley and AppContainer release checks. [Official release](https://github.com/horizon-rust/horizon/releases/tag/nightly)

## Build identity

The source was the verified release archive. On Ben-Desktop, the build directory was %TEMP%/mchorizon-nov20-integration/wasm-upstream/horizon-0a875da/.local/guest, with the pinned stock cargo-horizon-mod executable and Rust 1.99.1 (rustc 58cf8f9c35b355199727627d07ae34a4e2846d67). The x64 MSVC developer environment supplied link.exe for host build tools.

    cargo-horizon-mod horizon-mod wasm-std <output>/horizon-wasm-std.wasm
    cargo-horizon-mod horizon-mod build --wasm

The stdlib build took 21.10 s. The optimized GUI guest build took 10m 01s and the independent trap fixture took 9m 50s. The .hmod archive passed its integrity test and the manifest SHA-512 matched the embedded module. The guest carries dylink.0 and horizon.build and exports horizon_layout, horizon_in and horizon_hook. [Build logs](../deliverables/nightly-2026-11-20/wasm/guest.log) · [ABI data](../deliverables/nightly-2026-11-20/wasm/wasm-abi.json) · [Stock package check](../deliverables/nightly-2026-11-20/wasm/stock-package-check.json)

| Artifact | Size (bytes) | SHA-256 |
| --- | ---: | --- |
| Local shared stdlib | 652,958 | c1d04e08fb402134fb849ae6cb5d5af518c1e5612d715a75b3757c2a5bd3ec5f |
| Standard GUI fixture | 2,851,960 | 295326eb0dca7d0ffd9e7ca45b6a4aa31a7284ea818fafc50c804225dd89f3bc |
| Standard trap fixture | 2,851,895 | 7bbb95baf4117ee7495ec731f16e7c8ab3745862ef44de58efde53b832e7d53a |
| Published shared stdlib | 652,958 | 3c647d91c7c943ffb65a31ec406392de328daa7a6d2779e44e71c3561068b7dc |
| Stock game executable | 161,355,776 | 2b685d64b5c253eef2a03f7cc5ec7c141984fc543d18cd009079444ab3ef2e21 |

The official stdlib was also independently read from the x64 release ZIP by HTTP byte range. Its compressed location and CRC32 matched the central directory and it was byte-identical to the launcher-installed stdlib. The full ZIP transfer timed out. The targeted member check completed. The expected ZIP SHA-256 in the signed upstream checksums is 5514bc94f1d2c9b370f4444726236ad9e303d2d34e1323593393e7ebe07abac3. The two stdlibs differ by 252 path separators in the Wasm data section. The code, imports, exports and other sections match. The Builder candidate pins the locally regenerated stdlib. [Member receipt](../deliverables/nightly-2026-11-20/wasm/official-windows-stdlib-range-receipt.json)

## Stock x64 runs

The exact Nightly binary was copied to a private test folder with the regenerated stdlib. It used the existing 26.3 prepared resources, offscreen DX12 on an RTX 3090 Ti, a requested 960 × 540 frame size and isolated per-run game and settings folders. All screenshots were 1440 × 810. Audio and telemetry were disabled. The enabled run waited for a local creative world seeded with 1234 to finish loading at a two-chunk render distance. The control kept the identical package present with its enabled setting false.

| Run | Result | Exact magenta pixels in upper-right area | Elapsed |
| --- | --- | ---: | ---: |
| Enabled, new world | Stock host instantiated the mod. 49 chunks loaded, 42 rendered frames at status capture, save file written and clean exit. | 2,304 | 119.30 s |
| Disabled, title | Title rendered and game exited cleanly. | 0 | 18.87 s |
| Trap and GUI enabled, title | The runtime loaded two mods. The trap's unreachable instruction stopped its group. The separate GUI fixture drew and the game exited cleanly. | 2,304 | 122.78 s |

[Enabled world capture](../deliverables/nightly-2026-11-20/wasm/stock-enabled-world.png) · [Disabled control](../deliverables/nightly-2026-11-20/wasm/stock-disabled-title.png) · [Runtime results](../deliverables/nightly-2026-11-20/wasm/stock-runtime-results.json) · [Trap log](../deliverables/nightly-2026-11-20/wasm/stock-trap-title.log)

## Signed UWP game runs

The final signed x64 and x86 packages consumed the locally regenerated standard library and loaded the pinned GUI fixture through Pulley. The Wasm marker and the native marker drew together in actual saved worlds on x64 D3D11, D3D12 and ANGLE, and x86 D3D11. Mobile in-world hooks, UWP host limits and trap isolation require separate runtime evidence. The desktop restart controls changed the enable settings but failed to produce the world frame needed for visual comparison. [Signed game evidence](nightly-cross-architecture-2026-11-20.md)

## Runtime limits in a signed UWP game

A signed x64 D3D11 release with the launcher startup fix loaded three Wasm fixtures in the isolated saved world. The trap fixture hit its intentional unreachable instruction and stopped only its own group. A separate probe requested about 128 MiB of additional guest memory against its 32 MiB group ceiling. Wasmtime refused the growth at 129 MiB total. The same group was later interrupted after the stock 100-tick execution deadline. This Horizon revision enforces the hook's time budget with Wasmtime epochs and does not enable instruction fuel. No budget or linker changes were applied for the test. The independent Wasm and native markers still rendered and the game exited cleanly. [Signed game log](../deliverables/nightly-2026-11-20/runtime-evidence/x64/wasm-limits-and-trap/game.log) · [World result](../deliverables/nightly-2026-11-20/runtime-evidence/x64/wasm-limits-and-trap/result.json)

The resource probe SHA-256 is `e60f739c0efe9bead7d2ff6291ef3ea7b48042917b26156807c656e1da6d2cf4`. Ben-Desktop used the pinned November 20 cargo-horizon-mod and Rust 1.99.1 to build it from the fixture in 9m 44s. The source contains a bounded workload and a single memory request that the configured ceiling refuses. The reusable build script accepts `-Fixture Limits`. [Fixture](../uwp/wasm-abi-probe/guest-limits/) · [Build log](../deliverables/nightly-2026-11-20/wasm/limits-build.log)
