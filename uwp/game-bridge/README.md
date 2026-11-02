# Horizon game bridge

This static library exposes the real Horizon game loop to the UWP host through
`horizon_uwp_run`. The host supplies absolute UTF-16 paths to the prepared
resources and an app-local writable game directory. Startup errors are passed
back to the host for logging in app-local storage.

The bridge uses the local UWP winit and wgpu-hal ports. It has linked and run
inside an x64 and an ARM64 UWP host, with separate experimental rendering paths. Audio and HTTPS require their
explicit UWP features and matching native host adapters.

Build with a desktop MSVC developer environment so Cargo can build its host
tools, using a copy of the patched Rust source described in
`../rust-std/README.md`. For example, with the prepared library directory:

```powershell
$env:RUSTC_BOOTSTRAP = '1'
$env:__CARGO_TESTS_ONLY_SRC_ROOT = '<absolute path to patched rust-src/library>'
cargo build --manifest-path uwp/game-bridge/Cargo.toml --release `
  -Z build-std=std,panic_abort --target x86_64-uwp-windows-msvc
```

Use `aarch64-uwp-windows-msvc` for ARM64. Cargo's internal source override is
unsupported and is tied to the toolchain version; the tested patches match
Rust 1.99.1. Build the native host and package the resulting static library
as described in `../host/README.md`. The native D3D11 path has also run on ARM32.
