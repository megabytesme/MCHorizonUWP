# Horizon core UWP bridge

This static-library probe exposes a few `horizon-core` functions through a C ABI.
It is not the Horizon client or a playable game.

From a desktop MSVC x64 developer prompt with nightly Rust and `rust-src`:

```bat
cargo +nightly build --manifest-path uwp\core-bridge\Cargo.toml -Z build-std=std,panic_abort --target x86_64-uwp-windows-msvc --release
```

The output is `target/x86_64-uwp-windows-msvc/release/horizon_uwp_core_bridge.lib`
under this directory. Cargo build scripts run on the desktop host, so use a
desktop compiler environment for this command; the Rust target selects UWP.

To link the bridge into the x64 host, set the environment variable below and
rebuild the host. The host checks the Horizon protocol version at startup.

```powershell
$env:HORIZON_UWP_BRIDGE_LIB = (Resolve-Path uwp\core-bridge\target\x86_64-uwp-windows-msvc\release\horizon_uwp_core_bridge.lib).Path
cmd /c uwp\host\build-native.cmd x64
```
