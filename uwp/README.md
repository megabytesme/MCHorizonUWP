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
