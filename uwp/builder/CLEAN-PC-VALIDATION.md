# Fresh-PC builder validation (2026-11-05)

The complete clean-PC acceptance test has **not passed**. The builder still
needs automatic compiler and SDK provisioning, Minecraft asset download and
preparation after real-account sign-in, and a full installation trial. These
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
- The builder's local-install helper installed a separately signed x86 proof
  package and its Microsoft dependencies for the Sandbox user. The test used
  a certificate already trusted in Sandbox; the interactive first-time Windows
  administrator prompt has not been tested.

## Build records

The following folders are on Ben-Desktop under `$env:TEMP`; they are retained
for inspection and are not release artifacts. The actual builder workspaces on
a user's PC are separate. The builder now limits parallel Cargo jobs when
available memory is low, after an ARM32 rebuild hit Windows' paging-file limit.

| Target | Proof workspace | Result |
| --- | --- | --- |
| x64 | `horizon-split-recipe-proof-fcece00382d041cb9f05e30d4b67efa4` | Rust bridge, host, signed client and resource framework built; package installed in Sandbox. |
| x86 | `horizon-x86-sdk-build-20261105` | Rust bridge and strict host built, package signed and installed in Sandbox after adding x86 VC runtime. |
| ARM32 | `horizon-arm-sdk-build-20261105` | Rust bridge and strict host built with VS 2022 and SDK 17763; signed package signature verified in Sandbox. |
| ARM64 | `horizon-arm64-sdk-build-20261105` | `build.cmd` built the Rust bridge; `uwp\host\build-game.cmd arm64` strict-linked the host; signed package signature verified in Sandbox. |
| x64 with Zoom | `horizon-mixins-apply-proof-20261105` | `build-modded.cmd` built the UWP bridge from the transformed source. |

The bridge build uses `cargo +stable build --offline --manifest-path
uwp/game-bridge/Cargo.toml` with the selected target and UWP features, after
fetching dependencies and preparing the patched Rust standard library. ARM32
also uses its offline dependency overrides. The host and package scripts use
Microsoft's SDK tools. These proofs were run on a prepared development machine,
not through a downloaded builder on a blank PC.

## Remaining work

- Provision and verify the supported Visual C++ UWP toolchain, SDKs, Rust and
  rust-src automatically; allow Windows to request elevation when
  necessary. Current scripts assume specific Visual Studio locations.
- Complete and test Minecraft asset download and preparation after ownership
  sign-in, including the privacy of generated resources. Test sign-in in the
  UWP client separately.
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
