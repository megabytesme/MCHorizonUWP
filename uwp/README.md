# Horizon UWP port experiments

This directory contains porting experiments, not a playable Horizon UWP client.
The native shell creates a UWP CoreWindow and presents a solid color through
D3D11. It can optionally call the small `horizon-core` static-library bridge.
It does not start Horizon's game loop or render the game.

- [Host build and packaging](host/README.md)
- [Rust core bridge](core-bridge/README.md)
- [Window and event-loop integration](WINDOWING.md)
- [Experimental Rust standard-library patch](rust-std/README.md)
- [ARM build and deployment results](../uwp-arm/README.md)

The bridge-linked shell started on x64 Windows and on a Surface Pro X. Its
rendered output has not been visually verified. A signed ARM32 prototype was
produced for testing, but has not been installed on Windows 10 Mobile.
Horizon itself still requires UWP window, graphics, and platform API work.

UWP resource-pack overlays cannot create desktop directory junctions. The
platform helper reports them unsupported, and the existing overlay code
mirrors the affected directories instead.
The UWP clipboard currently retains copied text inside the game only; opening
external links or folders from the game still needs a UWP launcher integration.
The package declares outbound Internet and private-network access for multiplayer.
The UWP game does not send Horizon's automatic activity ping.

The unmodified client also completed an x64 release build targeting
`x86_64-uwp-windows-msvc` using Rust 1.99.1, `RUSTC_BOOTSTRAP=1`, `-Z
build-std=std,panic_abort`, and the desktop MSVC/SDK link environment. The
resulting `target/x86_64-uwp-windows-msvc/release/horizon-client.exe` is not a
working UWP application: PE inspection found desktop imports including
`CreateWindowExW`, `RegisterClassExW`, `CreateProcessW`, and `GetStdHandle`.
This build used the original wgpu backend, before the experimental ANGLE
changes were made. Compilation does not validate AppContainer compatibility.
