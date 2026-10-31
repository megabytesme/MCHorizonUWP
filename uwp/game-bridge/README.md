# Horizon game bridge

This static library exposes the real Horizon game loop to the UWP host through
`horizon_uwp_run`. The host supplies absolute UTF-16 paths to the prepared
resources and an app-local writable game directory. Startup errors are passed
back to the host for logging in app-local storage.

The bridge uses the local UWP winit and wgpu-hal experiments. Building it does
not by itself establish that the complete game links or runs in AppContainer.
The graphics, window, and Rust standard-library ports must also be built and
packaged with the host.
