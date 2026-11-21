# FL9_1 world scaling probe

Copy this directory to the system temp folder. Put `fl91_world_scale.rs` and `fl91_world_scale.wgsl` from `crates/horizon-client-render/src/gfx` of the assembled November 20 port in its `renderer` subdirectory. The fixture compiles the production scaler directly. The small `GpuChecks` adapter in the fixture matches the renderer's validation and out-of-memory scopes.

The desktop script uses the pinned fork artifacts from the prior DX11 public tests, whose target directories are under `%TEMP%\wgpu-dx11-depth-x86-x64-20261120`. For the desktop library build commands, see the [wgpu regression evidence](../../arm-hud/build/final-mobile/desktop/). The script first forces a physical FL9_1 device, then uses the fork's FL9_1 shader projection on the adapter's native level.

```powershell
.\build-desktop-probe.ps1 -Architecture x64
.\build-desktop-probe.ps1 -Architecture x86
```

The UWP scripts use Rust 1.99.1 and the production UWP release rlibs. The ARM32 run uses `E:\Temp\mchorizon-mod-runtime-mission\target-arm32-wasm-focused`. ARM64 uses `E:\Temp\mchorizon-mod-runtime-mission\target-cross`. `$assembledSource` points to a Builder assembly that contains the host kit for the chosen architecture. `$certificate` is the existing device test certificate from the current user's personal store. Use the package paths and publisher from the build output.

```powershell
.\build-rust-uwp.ps1 -Architecture $architecture -CargoTargetRoot $cargoTarget -OutputRoot $output
.\build-uwp.ps1 -Architecture $architecture -SourceRoot $assembledSource -Publisher $certificate.Subject -OutputRoot $output
Copy-Item -LiteralPath "$output\uwp-$architecture\WgpuWorldScaleProbe-$architecture.unsigned.appx" -Destination $signedProbe
& $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint $signedProbe
& $signtool verify /pa $signedProbe
```

The signed probe requires no game assets. Launch it from the interactive desktop session or Device Portal. It writes its measurements to `LocalState\wgpu-world-scale-probe.txt` and exits. `overall=PASS` confirms all eight cases. On Surface, `run-surface-probe.ps1` is the interactive install and activation script for the recorded signed artifact.
