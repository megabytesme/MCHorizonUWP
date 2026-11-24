# November 20 Narrator candidate

Patch 0.1.19 and client 0.1.19 form a separate candidate for the pinned November 20 Horizon source 0a875da91dbb0fdcb7ba8c42c11341aa229af51a. They retain the 0.1.18 HDR renderer inputs and Builder 1.0.10. Existing release recipes and the signed catalog are unchanged.

The game uses Windows speech synthesis with a voice selector in Options > Mods. The launcher shares that preference and can preview it. The patch and client must be used together. The game bridge imports seven narrator functions and the WinRT screenshot publisher, so the client archive requires a freshly built four-architecture host kit. The candidate tool checks for those names in each host executable and import library. Complete strict linking and runtime validation separately.

For a capture that must preserve existing files, the writer closes the staged PNG before publishing it. A direct same-directory move remains the normal path. Permission-denied and unsupported moves try WinRT with FailIfExists. A collision, invalid input or write failure reaches the existing error handling. The broker uses a single destination filename and adds no Pictures Library permission. World-icon replacement and other replacement-enabled captures retain their current behavior.

To prepare the original November 9 Narrator patch, specify --patch-version 0.1.5 and --patch-path patches/main-2026-11-09/horizon-uwp-0.1.5.patch. That path pins e797c725ad99ef311db216de7f41a2e6593353db. The default 0.1.5 Nightly recipe retains its November 18 source pin.

Prepare a host kit from this source with tools/build_host_artifact_kit.ps1 -OutputDirectory <new-directory> -SourceRoot <source-directory>. After committing the source and patch, run tools/prepare_candidate.py with --revision <commit>, --patch-version 0.1.19, --host-artifacts <host-kit>, --wgpu-archive <pinned-wgpu-zip>, --wasmtime-archive <pinned-wasmtime-crate>, --wasm-standard-library <pinned-nov20-wasm>, --upstream-archive <pinned-upstream-zip>, --upstream-repo <upstream-git> and --output <new-directory>. This prepares unsigned artifacts only and verifies the existing input pins.

The x64 desktop and reduced-feature UWP client Cargo checks passed, as did x64 compile-only checks for the host and XAML shell. These checks do not verify a full-feature bridge link, packaged speech playback, voice settings on devices, ARM builds, or a signed release.
