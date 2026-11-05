# Fresh-PC builder validation (2026-11-05)

The complete clean-PC acceptance test has **not passed**. The builder still
needs a full installation trial. Rustup, Visual Studio and SDK provisioning are
implemented, but their complete clean-Sandbox validation is not yet established.
The Visual Studio setup test was interrupted when the host unexpectedly restarted;
it had not finished. Windows did not record a crash code.
Minecraft asset preparation has been exercised on the development machine,
but not yet as part of a complete clean-PC build. These
results do not establish device compatibility.

## Component results

- An 8192 MB Windows Sandbox opened the self-contained WinUI 3 builder. The
  light theme, architecture default and native-mod search, selection, filtering
  and removal were exercised through UI Automation. Git and Python were
  installed with WinGet in the Sandbox. WinGet was installed there separately
  because the user assumes it is available on a normal PC.
- The user signed in through the Sandbox builder with an account that owns
  Minecraft: Java Edition and confirmed that the ownership check succeeded.
  Asset preparation and the UWP client's own sign-in were not tested there.
- In an empty Sandbox test profile, the builder downloaded the pinned WinUI
  2.7.3 NuGet package, verified its digest, extracted its files, and reused the
  cache on a second call. The Microsoft UI and VC runtime APPXs from the SDK
  were signature and architecture checked for x86, x64 and ARM64. The ARM32
  VC runtime APPX was absent from the development machine's SDK installation.
- The Sandbox also installed LLVM 23.1.4 through the builder's WinGet setup
  path and confirmed that `clang.exe` was present afterwards.
- A later fresh Sandbox started the Visual Studio 2022 Build Tools installation
  through WinGet. Its installer verified the download and progressed into the
  Windows 11 SDK before the host restart interrupted it. No completion result
  or clean-PC build was recorded.
- A reset 8192 MB Sandbox downloaded and verified the Microsoft VC runtime
  framework fallback for all four architectures. This component test used a
  shared copy of Microsoft's SignTool, so it is not an end-to-end clean-PC
  build. The ARM32 package is Microsoft-signed version 14.0.33519.0.
- On the development PC, UI Automation checked that Next on Welcome shows
  the dependency consent dialog and that declining advances to the target page
  with one of six pages marked complete. The dependency expander exposed eight
  separate progress bars. No dependency installer was run in this UI check.
- A strict x64 UWP host link succeeded using a private builder WinUI profile
  instead of the user's NuGet cache; its retained output is in
  `E:\temp\HorizonPrivateProfileHostProof-20261105`.
- The Sandbox verified the public signed catalog and assembled the published
  x64 source release from the upstream archive, patch and client prereleases.
  An earlier download ended with an unexpected EOF; interrupted and stalled
  transfers are now retried and every artifact must pass its SHA-256 check.
- An automated approval review blocked the separate attempt to download and
  launch the published builder ZIP inside Sandbox, reporting only "blocked by
  policy". The wizard checks above used the locally published builder directory.
- Horizon's own registry downloader staged the Zoom native mod. The builder's
  mod pipeline applied it to the UWP source and restored the crate setting
  needed for compilation. An independently staged copy of that same modified
  game source compiled its x64 UWP bridge successfully. Other registry mods
  and modded packages on real devices are not validated.
- Developer-machine proofs produced and signed client APPXs for x64, x86,
  ARM32 and ARM64, using private prepared Minecraft resources. The x64 and x86
  packages installed in Sandbox with their matching Microsoft UI and VC runtime
  frameworks and a neutral resource framework. The ARM32 and ARM64 signatures
  verified in Sandbox; their target architectures cannot be run there. No game
  was launched in these checks.
- A private development-machine test downloaded and checked the pinned 26.3
  resources and used Horizon's preparer to produce resources including sounds.
  The integrated builder helper repeated the download and preparation in
  `E:\temp\HorizonUWPAssetProof-20261105`. The owner confirmed Minecraft
  ownership separately in the Sandbox. This is not an end-to-end Sandbox build.
- The builder's local-install helper installed a separately signed x86 proof
  package and its Microsoft dependencies for the Sandbox user. The test used
  a certificate already trusted in Sandbox; the interactive first-time Windows
  administrator prompt has not been tested.

## Build records

The following records were made on Ben-Desktop under `$env:TEMP`; those
temporary folders are no longer present. They were not release artifacts.
The actual builder workspaces on a user's PC are separate. The builder limits parallel Cargo jobs when
available memory is low, after an ARM32 rebuild hit Windows' paging-file limit.

| Target | Proof workspace | Result |
| --- | --- | --- |
| x64 | `horizon-split-recipe-proof-fcece00382d041cb9f05e30d4b67efa4` | Rust bridge, host, signed client and resource framework built; package installed in Sandbox. |
| x86 | `horizon-x86-sdk-build-20261105` | Rust bridge and strict host built, package signed and installed in Sandbox after adding x86 VC runtime. |
| ARM32 | `horizon-arm-sdk-build-20261105` | Rust bridge and strict host built with VS 2022 and SDK 17763; signed package signature verified in Sandbox. |
| ARM64 | `horizon-arm64-sdk-build-20261105` | `build.cmd` built the Rust bridge; `uwp\host\build-game.cmd arm64` strict-linked the host; signed package signature verified in Sandbox. |
| x64 with Zoom | `horizon-mixins-apply-proof-20261105` | `build-modded.cmd` built the UWP bridge from the transformed source. |

The ARM32 bridge and strict host were also rebuilt with VS 2022 and SDK 19041
in `horizon-vs2022-host-proof-20261105`, limiting Cargo to three jobs after a
32-job attempt exhausted the Windows paging-file limit. This was a developer
machine toolchain check, not a clean-PC package build.

The bridge build uses `cargo +stable build --offline --manifest-path
uwp/game-bridge/Cargo.toml` with the selected target and UWP features, after
fetching dependencies and preparing the patched Rust standard library. ARM32
also uses its offline dependency overrides. The host and package scripts use
Microsoft's SDK tools. These proofs were run on a prepared development machine,
not through a downloaded builder on a blank PC.

## Remaining work

- Finish the clean-PC test of Visual C++ UWP tools and SDK setup. Test automatic
  Rustup installation and installation of the pinned Rust toolchain and rust-src
  as part of the full builder flow.
- Test Minecraft asset preparation after ownership sign-in in the clean-PC
  flow, including the privacy of generated resources. Test sign-in in the UWP
  client separately.
- Test the local installation offer and first-time certificate approval end to
  end, and complete installation guidance for other devices. Run the client on
  real target hardware, including ARM32.
- Validate more native mods, including different mixin revisions and bundles
  with data or post-build steps. Unsupported cases must continue to fail clearly.
- Run the entire builder flow in a fresh Sandbox, with no preinstalled
  development tools or prepared resources, for every architecture. Sign-in
  requires an account that owns Minecraft: Java Edition.

The original first Sandbox attempt is recorded in Git history. It found missing
WinGet, a missing PRI file and a startup selection problem, which have since
been addressed for the preview; it did not build a package.
