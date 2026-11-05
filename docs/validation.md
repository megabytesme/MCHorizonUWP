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
fresh-download test. Python's silent installer and Rustup remain unverified.

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

## 2026-11-05: catalog signing component

In Sandbox, a separate ephemeral self-signed P-256 certificate was created in
the Sandbox user's certificate store. The maintainer tool exported its public
key, signed the candidate catalog and verified the signature. The private key
was not exported. This does not establish a production trust key or publish a
signed catalog.

## 2026-11-05: user-owned publisher support

The x64 UWP host was rebuilt from the assembled test source with the resource
publisher taken from the installed package identity. Its strict link and
unsupported-import checks passed. A packaging probe then created a synthetic
resource framework and a dependent client with a non-prototype publisher; both
package manifests and the framework dependency used that publisher. This is a
component test and does not yet exercise the builder's signing pipeline.

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
