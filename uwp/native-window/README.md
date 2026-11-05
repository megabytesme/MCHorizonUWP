# UWP native window owner

`NativeRenderer::from_host_window()` is the safe entry point for a Rust UWP
client that forbids unsafe code. It asks the UWP host for the current
CoreWindow; the host returns null unless the call is on the UI thread that
owns that window. The wrapper acquires its own COM reference and keeps it
alive until after its Direct3D renderer is dropped. The owner and underlying
renderer are both thread-bound, so they cannot be moved or shared across
threads.

The host must supply the `horizon_uwp_window_handle` symbol with this exact
contract: return a borrowed, live CoreWindow interface only on the owning
thread and null otherwise. This is a trusted internal ABI, not an entry point
for arbitrary external pointers. A missing window or a call on another thread
returns an error. The actual constructor and swap chain still require a valid
UWP CoreWindow, hardware D3D11 and an active UI thread; a cargo check alone
does not verify creation or presentation on a device.

The game bridge forwards its `native-d3d11` feature to `horizon-client`.
That opt-in path remains experimental while the native game renderer is
integrated.

For development builds using `native-d3d11-readback`, creating an empty
`LocalState/game/native-world-smoke.enable` marker starts the creative world
`NativeSmoke`. Its game files are isolated under
`LocalState/game/native-world-smoke`; remove the marker to return to normal
startup. Builds without the readback feature ignore the marker. The smoke run
keeps running until closed so its world and logs can be inspected.
