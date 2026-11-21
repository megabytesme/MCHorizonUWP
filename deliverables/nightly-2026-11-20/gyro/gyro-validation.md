# November 20 Nightly gyroscope validation

The standalone mod targets Horizon `0a875da91dbb0fdcb7ba8c42c11341aa229af51a`, game 26.3, API 0.2 and mixins `e59da9094f0c2fdda6c627fe9eff22dec6a906f0`. Its source uses the official `render_frame_pre`, `EntityRef::rotation` and `ClientHost::set_local_rotation` APIs. The standalone project owns activation, settings, filtering and calibration. The UWP provider supplies bounded, timestamp-integrated angular deltas in display-aligned axes. See `uwp/mods/horizon-gyro-aim/README.md` for controls and physical test steps.

The trusted native source validator accepted the manifest and its API-only Cargo layout against the November 20 SDK pins. The three host-side Rust tests cover angular axes, independent sensitivity and inversion, the stationary deadzone, smoothing reset, delta bounds and rejecting movement during zero-bias calibration. All four standalone Rust targets passed source type checks against the pinned Nightly API. These checks establish source compatibility. Package linking and gameplay need a signed engine build of the same revision.

## UWP host

Ben-Desktop built the host kit from the product source in `%TEMP%\mchorizon-nightly-2026-11-20-gyro`. Run `tools/build_host_artifact_kit.ps1 -SourceRoot <product-source> -OutputDirectory <new-directory> -Architecture <arm, arm64, x86 or x64>` for each architecture. The run used the existing architecture SDK selections in that script.

The generated ARM32, ARM64, x86 and x64 executables passed the host's strict AppContainer linker. Each PE has the expected machine type and AppContainer flag, and exports the gyro start/stop and sample functions. The ARM32 executable's imported DLL names and their symbol pairs match the known-good November 18 Patch 0.1.12 host. `prebuilt-host-kit-exe-proof.json` records the four executable hashes. `prebuilt-host-kit.sha256` covers the entire host kit, and the paired `lumia-old-imports.txt` and `lumia-gyro-clock-imports.txt` hold the import dumps.

## Physical device evidence

- Ben-Tablet returned an actual WinRT `Gyrometer` with a 2 ms minimum reporting interval. Its `OrientationSensor` and `Accelerometer` were also available. See `surface-sensor-availability.json`.
- Ben-Desktop returned no default WinRT gyrometer, orientation sensor or accelerometer. See `desktop-sensor-availability.json`.
- The Lumia Device Portal initially responded with `Windows.Mobile`. A later authenticated read using the pinned-certificate PowerShell 5.1 helper timed out at `GET /api/os/devicefamily` before the app inventory request. See `lumia-portal-status.json` for the follow-up.
- Actual in-world yaw and pitch from physical motion, both landscape orientations, the disable/re-enable test and coexistence with touch and controller require the signed Nightly package and a person holding the device.
