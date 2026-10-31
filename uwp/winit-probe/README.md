# UWP winit + wgpu presentation probe

This standalone static library drives the vendored UWP winit backend and clears
a wgpu surface through ANGLE. It does not run Horizon. On x64 the native host
strict-linked with no retained forbidden API sentinels, and a loose UWP package
ran and wrote `frame-presented.txt` after its first wgpu presentation. Visual
output and ARM64 have not yet been verified for this probe.

To build, first prepare the copied Rust standard library as described in
`../rust-std/README.md`. In a desktop x64 MSVC developer prompt with
`RUSTC_BOOTSTRAP=1` and `__CARGO_TESTS_ONLY_SRC_ROOT` pointing to that copied
library, run:

```bat
cargo +stable build --manifest-path uwp\winit-probe\Cargo.toml -Z build-std=std,panic_abort --target x86_64-uwp-windows-msvc --release --target-dir uwp\window-bridge\rust\target
```

Then enter an x64 UWP MSVC environment with SDK 15063 and run
`powershell -ExecutionPolicy Bypass -File uwp\winit-probe\build-host.ps1`.
Package the resulting executable with compatible UWP ANGLE DLLs and the host
manifest/assets. The locally used deprecated ANGLE.WindowsStore DLLs are for
diagnosis only and are not included in the repository.
