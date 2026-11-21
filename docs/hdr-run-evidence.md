# Verify archived D3D12 HDR runs

Run `tools/verify_hdr_run.py` with Python 3.11 or later after `Test-HdrGame.ps1` closes the isolated game. Keep the full output directory with `result.json`, `lifecycle.log`, `game.log` and the game PNGs. Supply the original APPX and its trusted package receipt.

```powershell
python ./tools/verify_hdr_run.py $Appx $PackageReceipt $Run --expect hdr --display '\\.\DISPLAY2'
python ./tools/verify_hdr_run.py $DiagnosticAppx $RecoveryIndex $RecoveryRun --expect sdr --case device-recovery
python ./tools/verify_hdr_run.py $Appx $PackageReceipt $LifecycleRun --expect hdr --case display-lifecycle
python ./tools/verify_hdr_run.py $Appx $PackageReceipt $MonitorRun --expect hdr --case monitor-transfer
```

The verifier checks SHA-256 for the APPX, packaged host and game bridge, matches the run's package full name to the APPX manifest, and cross-checks the recorded D3D12 renderer and DXGI configuration against the archived logs. It checks the capture hashes and PNG structure, including BT.2020/PQ metadata and matching SDR and HDR companion filenames. `--display` compares the Windows display name from the run, which can be mapped to a physical monitor using the native display probe.

Run SignTool separately for signature verification. Archive the package install verification alongside the run to establish which exact bytes Windows installed. The existing run receipt identifies the process by package full name and PID. Those values alone cannot distinguish two builds with the same package identity and version.

`--case device-recovery` additionally checks native `DXGI_ERROR_DEVICE_REMOVED`, renderer recovery, subsequent panel configuration and the first present call. It requires a separate screenshot recorded by the game after recovery. Use the diagnostic APPX with direct F2 to collect that screenshot. The prior RDP pressure run has native removal and rebuilt-renderer evidence and returns `incomplete` because it has only a pre-removal image.

`--case display-lifecycle` checks the chronological HDR → SDR → HDR panel configurations, the PLMDebug suspended status for the matching package, the later HDR panel configuration after resume, and restoration of the Windows HDR setting. The archived harness captures an initial HDR pair. This case verifies later panel configurations from the logs, without claiming fresh gameplay captures for those states.

`--case monitor-transfer` checks that the harness moved the game window to a distinct SDR monitor and back. It requires the new SDR panel configuration and a fresh F2 SDR image between the initial and restored scRGB configurations.

Exit code `0` reports verified evidence for the requested case, `1` reports inconsistent or unreadable evidence, and `2` reports an incomplete run. Device removal and HDR configure rejection are separate cases. Native HDR rejection and repeated detach retry still require an instrumented candidate with explicit fault and rollback events.

## Stage native configuration faults

The rollback HAL calls DXGI `CheckColorSpaceSupport` and `SetColorSpace1` directly. The host recorder runs after the call and has no return value, and fresh chains attach to XAML after validation. The development-only stager materializes the two native call seams and the rollback-specific detach seam from the exact `4ca69dcd` assembly. It writes four files and a hash manifest into a new overlay directory.

```powershell
python ./tools/hdr/stage_panel_fault_probe.py $RollbackAssembly $FaultOverlay
```

Apply the overlay to a separate copy of the assembly, build both its x64 host kit and locked game bridge, then package them together with the isolated identity `MCHorizonUWP.HdrDev.PanelFault.x64`. Follow the development package procedure and verify the signed APPX. Use the same test world and fixtures as the rollback candidate. Keep the production source, Builder recipe and signed rollback package separate from the diagnostic.

With the HDR monitor active, create one empty request file in this package's `LocalState`. At a matching stage the diagnostic consumes the request and logs its site number.

| Site | Request | Action |
| --- | --- | --- |
| 0 | `hdr-panel-fresh-check.request` | Query DXGI with `DXGI_COLOR_SPACE_CUSTOM` on a fresh HDR chain. |
| 1 | `hdr-panel-reused-check.request` | Query the same color space on an already attached HDR chain. |
| 2 | `hdr-panel-fresh-set.request` | Attempt to apply `DXGI_COLOR_SPACE_CUSTOM` on a fresh HDR chain. |
| 3 | `hdr-panel-reused-set.request` | Attempt the same operation on an already attached HDR chain. |
| 4 | `hdr-panel-detach.request` | Simulate one detach error in the reused-chain rollback. Pair this with site 1 or 3. |

Arm a fresh request before launch and choose HDR On. For reused-chain trials start in HDR without a request, then arm the files and resize the same game window. A site 4 request only triggers during resized-chain error cleanup. Remove any unconsumed requests after each trial.

For each trial, require the consumed-site event plus the actual DXGI rejection or missing PRESENT flag, then a fresh SDR configuration and world screenshot while the same process remains on an HDR-active view. For a set trial the recorder shows `space=-1` and the HRESULT. For a check trial the renderer logs the native error or flags. A fresh rejected chain should never attach. A reused-chain detach failure should log the injected error followed by a successful pending-detach retry before configuration can continue. Keep a no-fault control and preserve the whole session log. A driver that accepts the injected call has not reproduced the intended rejection. This overlay has been staged offline and still needs signed runtime execution.
