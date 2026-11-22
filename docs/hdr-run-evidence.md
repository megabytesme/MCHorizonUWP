# Verify archived D3D12 HDR runs

Run `tools/verify_hdr_run.py` with Python 3.11 or later after `Test-HdrGame.ps1` closes the isolated game. Keep the full output directory with `result.json`, `lifecycle.log`, `game.log` and the game PNGs. Supply the original APPX and its trusted package receipt.

The harness preserves the same process's raw log parts under `game-log-parts` and combines them into `game.log` in order. This retains renderer and frame evidence when a run spans midnight or log rotation. The signed x86 run verified from its original pre-midnight gzip and post-midnight log, along with the original results and captures.

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

Run the trials independently under the desktop GPU lock. The game harness arms the requests in the isolated package, removes any unconsumed requests on exit, and captures complete PNGs via F2. Use a fresh output directory for each case and the direct-F2 Wasm fixture.

```powershell
$common = @{
    Identity = 'MCHorizonUWP.HdrDev.PanelFault.x64'; Hdr = 'On'; Backend = 'Dx12'
    Display = '\\.\DISPLAY2'; FixtureWorld = "$Fixtures/Nov20Smoke"; WasmFixture = "$Fixtures/f2.hmod"
}
& ./tools/hdr/Test-HdrGame.ps1 @common -PanelFaultCase Control -ExpectHdr -Output "$Evidence/control"
& ./tools/hdr/Test-HdrGame.ps1 @common -PanelFaultCase FreshCheck -Output "$Evidence/fresh-check"
& ./tools/hdr/Test-HdrGame.ps1 @common -PanelFaultCase FreshSet -Output "$Evidence/fresh-set"
& ./tools/hdr/Test-HdrGame.ps1 @common -PanelFaultCase ReusedSetFromSdr -Output "$Evidence/reused-set-from-sdr"
& ./tools/hdr/Test-HdrGame.ps1 @common -PanelFaultCase ReusedSetDetachHdrRetry -ExpectHdr -Output "$Evidence/reused-set-hdr-retry"
```

The fresh requests are armed before launch. For reused-chain coverage the harness starts in HDR, takes an HDR control image, then arms sites 3 and 4 and resizes the same window. A site 4 request triggers during resized-chain error cleanup. The control case checks that no fault request was consumed.

The fault cases require the consumed-site event plus the actual DXGI rejection or missing PRESENT flag, then a successful new chain configuration, XAML attachment and F2 capture from the same process. The renderer must stay on the HDR-active display. For a set trial the recorder shows `space=-1` and the negative HRESULT. For a check trial the renderer logs the native error or flags. A reused-chain detach fault requires the injected error and a successful pending-detach retry.

`ReusedSetFromSdr` arms only site 3 before launch, then checks that the attached chain was configured for SDR before attempting HDR and that SDR was restored. `ReusedSetDetachHdrRetry` requires the native rejection, the injected detach failure and a successful retry, then checks restoration of the already applied HDR mode and a new F2 pair. The SDR recovery cases require an SDR-only capture and fail if the game returns to HDR before capture.

### Signed x64 diagnostic results

The signed PanelFault APPX with SHA-256 `f5efdde398a088630c322e0b3039d0e6f76502744364b6526d105762c9dfbf54` passed SignTool and deployed with matching host and bridge payload hashes. The no-fault control confirmed FP16/scRGB and a fresh F2 pair on the RTX 3090 Ti and PG32UCDM.

FreshCheck and FreshSet left their requests unconsumed. The game configured an SDR panel during startup and reused the existing chain for its first HDR configuration. The fresh native seams remain untested.

`ReusedSetFromSdr` passed in PID 64924. On the HDR-active view, the same renderer configured BGRA8/sRGB before the HDR request. DXGI rejected `SetColorSpace1` with `0x80070057`. The renderer then configured and attached a new BGRA8/sRGB chain and saved a fresh SDR-only F2 capture. The offline verifier confirmed the captured PNG and archive hashes.

`ReusedSetDetachHdrRetry` passed in PID 71108. After native `SetColorSpace1` rejection with `0x80070057`, the injected detach error occurred once and the pending detach retry succeeded. The same renderer configured and attached a new FP16/scRGB chain. The process then saved a fresh SDR/HDR F2 pair, with both PNGs independently validated at 1098 × 720. An earlier trial with the same native fault sequence expected SDR from an already active HDR renderer and timed out. Its raw result records successful HDR reattachment and remains in the evidence archive.
