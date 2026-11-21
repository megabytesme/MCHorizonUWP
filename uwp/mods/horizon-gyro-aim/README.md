# Gyroscope Aiming

This native client mod targets Horizon Nightly `0a875da91dbb0fdcb7ba8c42c11341aa229af51a` and Minecraft 26.3. Build it with the UWP Builder's trusted native source workflow to use the host's Windows gyrometer provider. Select this source folder for Builder embedding on ARM32 or for a signed executable related set on ARM64, x64 and x86. Build the optional DLL for the same patched engine, compiler and architecture as its main package.

## In-game settings

Open **Options → Mods → Gyroscope Aiming**. Enable **Gyroscope aiming** to turn it on. It starts off. Horizontal and vertical sensitivity, axis inversion, smoothing in milliseconds and the deadzone in degrees per second are adjustable independently of the mouse. The default activation mode accepts device motion throughout normal gameplay. **Gyro only while using an item** limits it to use actions such as drawing a bow or raising a shield.

To recalibrate, toggle **Calibrate gyro when play resumes** and then return to gameplay holding the device still. The mod learns the resting bias and clears the request. Calibration keeps the existing camera pose. The mod displays a brief status when Windows cannot start a gyrometer. Menus, chat, focus loss, pause and a camera attached to another entity suspend gyro aiming.

The existing pregame **Touch gyro** settings apply to the legacy touch input mode. When the standalone mod is loaded, its **Options → Mods** settings control gyro input. Existing touch and gamepad movement and look controls can be used alongside the mod.

## Integration

The mod reads the local player's current rotation through `EntityRef::rotation` and applies deltas through the official `ClientHost::set_local_rotation` from `render_frame_pre`. The normal player sync sends the resulting rotation to the server. The game uses the same pitch limits and previous-frame state for the mod as for the supplied native rotation API.

The UWP host uses `Windows.Devices.Sensors.Gyrometer` and WinRT display-aligned axes. It integrates the reported angular velocities with their sensor timestamps and hands the mod a consumable pair of angular deltas. Orientation changes, visibility changes and a stale sample window clear pending motion. The base Windows 10 gyrometer API does not need a new package capability. The optional mod DLL resolves only the existing gyro start/stop and sample functions through the game host import library. Wasm imports and the official SDK method signatures stay the same.

An upstream motion provider could offer display-relative angular deltas to multiple platform adapters. A future Wasm implementation would need a separate user-controlled motion capability and a sample API with bounded rates. The existing gameplay rotation method already covers the application of those samples.

## Physical acceptance test

Use an isolated package profile, open a test world and jot down the starting F3 yaw and pitch. Enable the mod, rotate slowly left and right in landscape, then tilt up and down. Verify the axis and sign using F3 and repeat in the opposite landscape orientation. Stop moving, use touch, gamepad and mouse look, and verify control returns normally after handling menus, inventory, chat or a suspend/resume. Disable and relaunch to confirm stationary camera behavior with device motion, then re-enable and repeat. Write down the device, package and backend used with the results.
