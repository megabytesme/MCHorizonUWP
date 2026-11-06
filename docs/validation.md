# Validation notes

## 2026-11-05: first split-source x64 proof

The upstream source was assembled from a locally generated ZIP for Horizon
commit `f42ef42ef6ceccb3c6a20bea2481d87949a51c81`, the first patch candidate
and the separately packaged UWP source candidate. All 374 changed upstream
paths matched the port commit after normalizing Windows text line endings.
The local archive is a test fixture, not a downloaded release asset.

Separately, the GitHub source ZIP for the same upstream commit was downloaded,
its contents checked against that commit, and assembled with the candidate
patch and client archives. Its 374 changed paths also matched the port after
normalizing Windows text line endings. The build below used the local ZIP,
not this downloaded ZIP.

On Ben-Desktop, the build directory was
`%TEMP%\horizon-uwp-assembled-lf-20261105`. The patched Rust 1.99.1 source
was prepared with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
uwp/rust-std/prepare-source.ps1 -Destination .local/builder-rust-std -Toolchain
stable`. With the desktop x64 Visual C++ environment, `RUSTC_BOOTSTRAP=1`,
`__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS=nightly`,
`__CARGO_TESTS_ONLY_SRC_ROOT` pointing to that patched `library` directory,
and `CARGO_BUILD_JOBS` set to the available processor count, this command
passed:

```text
cargo +stable build --offline --manifest-path uwp/game-bridge/Cargo.toml --features native-d3d11,uwp-dx12-renderer,native-touch-mod,native-device-vibration-mod,uwp-audio,uwp-https,uwp-controller,xaml-shell -Z build-std=std,panic_abort --target x86_64-uwp-windows-msvc --release --target-dir .local/builder-target
```

An earlier attempt incorrectly used the UWP/store Visual C++ environment for
Cargo's desktop build tools and failed to link them; using the desktop x64
environment as above resolved that test setup error.

The version variables identified the 2026-11-03 nightly and its exact
upstream and port commits; the Microsoft application ID was empty. With the
resulting static library and the matching UWP feature environment variables,
`cmd /d /c uwp\host\build-game.cmd x64` passed the strict link and
unsupported-import checks. `uwp/host/package.ps1` then produced an unsigned
x64 APPX using existing private prepared resources, a temporary package
identity and the Microsoft UI Xaml 2.7 dependency.

As a separate manual proof, the APPX was copied into a Windows Sandbox,
signed with a Sandbox-only test certificate, verified after trusting its
public certificate **inside that Sandbox only**, and installed with its Xaml
dependency. The installed app was not launched. No real account sign-in,
builder-driven signing, automated dependency setup, or fresh-PC build was
tested. ARM32, ARM64 and x86 were not built in this run. These results must
not be represented as end-to-end release compatibility evidence.

## 2026-11-05: WinGet and Git setup

With WinGet installed separately in Windows Sandbox, the original builder
started the Git installer in interactive mode and it stopped at a Next button.
The updated builder requested silent Git installation and refreshed its
process PATH. Git installed without interacting with the installer, and the
same running builder subsequently reported Git as found. The Git installer
had been downloaded during the earlier interactive attempt, so this is not a
fresh-download test. A later WinGet command installed Python 3.13 silently in
Sandbox without interaction, and a fresh process refreshed PATH, skipped the
zero-byte Store execution alias and successfully ran that Python. The Python
test used the same fixed WinGet arguments as the builder but did not click its
install button. Rustup remains unverified and interactive.

## 2026-11-05: package signing component

In Sandbox, a standalone test harness created a user-owned signing identity,
reopened it in a later process, signed a synthetic framework APPX and exported
its public certificate. After the certificate was trusted inside that Sandbox,
SignTool verified the package and Windows installed it. The private key stayed
in the Sandbox user's certificate store. This component is not yet connected
to the builder's real package pipeline or installation guide.
The same Sandbox test subsequently signed a second synthetic package with the
same identity without exporting a second public certificate.
The package output component then created a signed client/framework pair and
one public certificate from synthetic inputs in the same Sandbox.
It also wrote an installation guide alongside those files; the guide calls out
the framework dependencies that the preview does not yet collect.

## 2026-11-05: native-mod browsing

The WinUI preview read the live Horizon registry inside Windows Sandbox and
listed 85 native projects advertising Minecraft 26.3. UI automation searched
for `zoom` and the list narrowed to two results. The index reader's tests also
cover revoked files, revoked projects and an unsupported schema. This checks
browsing only; neither UWP compatibility nor installation of registry mods has
been implemented or validated.

As a separate integration experiment, the published Horizon launcher 0.2.42
was checked against its GitHub release digest and run with isolated launcher,
game and desktop paths. It searched for Zoom and downloaded the `zoom.hnmod`
bundle into that scratch home using the launcher's own registry checks. The
launcher reported the bundle as unsigned and checked its SHA-256. It was not
compiled into a UWP app or installed by the builder.
The new staging component repeated that download in Windows Sandbox with a
pinned launcher archive and independently rechecked the resulting bundle's
digest. It remains a standalone component; the wizard and UWP build do not
yet use it.

## 2026-11-05: catalog signing component

In Sandbox, a separate ephemeral self-signed P-256 certificate was created in
the Sandbox user's certificate store. The maintainer tool exported its public
key, signed the candidate catalog and verified the signature. The private key
was not exported. This does not establish a production trust key or publish a
signed catalog.

The maintainer's user-owned catalog key was subsequently created on
Ben-Desktop. Its public key is embedded in the builder, and a locally signed
candidate catalog was verified with that embedded key. The private key was not
exported. The catalog points to draft releases that are not publicly
downloadable yet; rollback protection remains open.
The wizard can now select a verified catalog combination and start the pinned
split-source build. A Sandbox UI test confirmed that an unavailable catalog
leaves the local checkout option available; a live catalog-driven build has not
yet been tested.

## 2026-11-05: user-owned publisher support

The x64 UWP host was rebuilt from the assembled test source with the resource
publisher taken from the installed package identity. Its strict link and
unsupported-import checks passed. A packaging probe then created a synthetic
resource framework and a dependent client with a non-prototype publisher; both
package manifests and the framework dependency used that publisher. This is a
component test and does not yet exercise the builder's signing pipeline.

During a split-source build, MakeAppx rejected a prepared Minecraft resource
whose full staging path was 262 characters. A direct retry with Windows
extended-length paths succeeded. The packer now passes extended paths to
MakeAppx, retries a failure once, and retains a private diagnostic log if both
attempts fail. A synthetic framework test with a long resource path passed.

The split-source x64 build was then repeated successfully on Ben-Desktop using
`dotnet %TEMP%\horizon-builder-long-path-tests-20261105\Horizon.Builder.Tests.dll
--build-split-candidate %TEMP%\horizon-upstream-f42-official.zip
%TEMP%\horizon-release-candidate-shared-id-20261105\horizon-uwp-0.1.0.patch
%TEMP%\horizon-release-candidate-shared-id-20261105\uwp-client-0.1.0.zip
<private-resources>`. Its build directory was
`%TEMP%\horizon-split-recipe-proof-fcece00382d041cb9f05e30d4b67efa4`.
It compiled the game,
strict-linked the host, packaged the prepared resources separately, signed
both APPXs with a local identity and exported the public certificate. The
candidate inputs were verified local files; this did not test an authenticated
published catalog. In Windows Sandbox, after trusting that public certificate
inside the Sandbox, SignTool verified both packages and Windows installed the
client and framework. The app was not launched. This is not a fresh-PC builder
run, and no ARM32, ARM64 or x86 split-source build was performed.

## 2026-11-05: Horizon launcher client ID

The builder now supplies the public application ID used in the Horizon
launcher release build, rather than asking the player for an ID. The ID was
read from the public Windows launcher workflow log for `launcher@0.2.42`.
The core tests and WinUI builder compiled with this configuration; no account
was signed in during this check. On Ben-Desktop, `dotnet build` alone could not
locate the Visual Studio Appx tasks; `powershell -NoProfile -File
uwp/builder/build.ps1 -Configuration Release` selected the installed tasks and
built successfully from this repository. This does not test dependency setup on
a fresh PC. The same public ID was also found in the published Windows launcher
executable. The builder and UWP client now read one shared ID file. On
Ben-Desktop, the x64 UWP bridge was rebuilt from
`%TEMP%\horizon-uwp-assembled-lf-20261105` with the Cargo command above and the
client-ID environment variable unset. It passed in 8 minutes 8 seconds, and
the resulting static library contained the ID. A small standalone Rust test
also covered the default, explicit override and empty override. No account
was used and this was not a fresh-PC build.

## 2026-11-05: builder ownership check

The builder's Microsoft device-code and Minecraft ownership component passed
mocked Microsoft, Xbox and Minecraft service tests in Windows Sandbox, including
a pending sign-in, the alternate Minecraft login endpoint and an account
without ownership. In a separate live Sandbox UI test, Microsoft returned a
device code and the cancellation action worked. No real account or live
ownership check has been used, and the component is not yet connected to
resource downloads.

## 2026-11-05: alternative ARM32 toolchain compile

Microsoft [warns](https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads-archive)
that unserviced archived SDK installers, including the 15063 installer used by
the current ARM32 recipe, may be vulnerable. The supported downloads page
offers a [serviced 17763 SDK](https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads).
On Ben-Desktop, the already installed Visual Studio 2022 Build Tools v143 ARM
compiler and SDK 10.0.17763.0 compiled the actual UWP host, renderer, window
bridge and XAML shell in `%TEMP%\horizon-arm-serviced-sdk-probe-20261105`
using `cmd /d /c probe.cmd`. The shader compiler also produced the two feature
level 9_1 shaders. `llvm-readobj --file-headers App.obj XamlShell.obj` reported
ARMNT objects. The installed SDK on this machine was an older 17763 build;
the serviced installer was not installed in this test. A subsequent ARM32 Rust
bridge build completed in 6 minutes 18 seconds using the split candidate under
`%TEMP%\horizon-arm-sdk-build-20261105\source`, after selecting the 2022
`x64_arm store 10.0.17763.0` environment and the patched Rust source. The
command was:

```text
cargo +stable --config .local/arm32-windows-sys/patches-offline-arm32.toml build --offline --manifest-path uwp/game-bridge/Cargo.toml --features native-d3d11,uwp-dx12-renderer,native-touch-mod,native-device-vibration-mod,uwp-audio,uwp-https,uwp-controller,xaml-shell -Z build-std=std,panic_abort --target thumbv7a-uwp-windows-msvc --release --target-dir .local/builder-target
```

With temporary host and shell
scripts selecting that toolchain, `uwp\host\build-game.cmd arm` passed strict
link and unsupported-import checks. The ARM APPX was packaged against the x64
proof's neutral resource framework, signed and signature-verified in Sandbox;
its manifest declares ARM and the matching resource publisher. The Lumia was
not used. A separate x64 host link also passed with SDK 17763. The split-source
scripts now select this newer ARM toolchain; automatic provisioning and a
fresh-PC build are not yet validated.

## 2026-11-06: telemetry, updates and four-architecture hosts

On Ben-Desktop, `uwp/builder/build.ps1 -Configuration Release -Publish` produced the
self-contained WinUI builder. The script supplies the installed Visual Studio AppxPackage MSBuild
paths when invoking `dotnet`; plain `dotnet build` selected the .NET SDK's missing PRI task path.
The builder setup tests passed, and `python tools/check_public_tree.py` passed. The publish emitted
two non-fatal ruleset warnings.

The source patch at `patches/nightly-2026-11-03/horizon-uwp-0.1.1.patch` passed `git apply --check`
against Horizon commit `f42ef42ef6ceccb3c6a20bea2481d87949a51c81`. After overlaying the UWP client
tree, `tools/build_host_artifact_kit.ps1` built and strictly linked the x86, x64, ARM64 and ARM32
hosts on Ben-Desktop. The output kit is `E:\Temp\MCHorizonHostKit-2026-11-06-client-fixed5` (4.9
MiB). It used SDK 10.0.19041.0 for x86, x64 and ARM32, and 10.0.26100.0 for ARM64. This validates
the native hosts and their import checks; it does not build the Rust game bridge or the four real
game APPXs.

A synthetic x64 packaging probe used `notepad.exe` only to check packaging. MakeAppx unpacked the
unsigned APPX and confirmed `HorizonRelease.json` carries the selected patch, client and upstream
identity. It is not a playable Horizon package. In WSL, telemetry payload and consent tests passed
(46 tests and 7 consent integration tests; one telemetry test was ignored), focused bug-report tests
passed 3/3, and release-check archive-layout tests passed 8/8.

The Oracle receiver returned `ok` from `https://horizontelemetry.bytesdirect.com/healthz` over both
IPv4 and IPv6. No test telemetry was submitted. Local patch 0.1.1, client 0.1.2, builder 0.1.3 and
a signed candidate catalog are staged under `E:\Temp\MCHorizonReleaseCandidate-2026-11-06`; no
new GitHub release was published, and the repository catalog remains on its existing published
versions until the release assets are uploaded.
