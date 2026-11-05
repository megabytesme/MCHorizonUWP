# Validation notes

## 2026-11-05: first split-source x64 proof

The upstream source was assembled from a locally generated ZIP for Horizon
commit `f42ef42ef6ceccb3c6a20bea2481d87949a51c81`, the first patch candidate
and the separately packaged UWP source candidate. All 374 changed upstream
paths matched the port commit after normalizing Windows text line endings.
The local archive is a test fixture, not a downloaded release asset.

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
