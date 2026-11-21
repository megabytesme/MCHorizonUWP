# Interactive Surface Pro X Nightly QA

Run this harness in Windows PowerShell 5.1 on the Surface with its console unlocked. Install the signed ARM64 Nov20 development main package, its signed native-mod related set and the matching resource framework first. The harness verifies the installed build and isolated test flag before targeting its own window. It uses the current user and the active console.

Pass a fixture directory holding the standard pinned `uwp_wasm_hook_probe.hmod`, the optional `uwp-smoke.hmod` for content tests, and a trusted `world-fixture-bootstrap/Nov20Smoke` directory with its `fixtures.json` digest manifest. The fixture may include initial `horizon-graphics.txt` and `options.txt`. Keep the copied test fixture outside the production saves.

```powershell
$test = @{
    Architecture = 'arm64'
    Mode = 'World'
    PackageVersion = '1.0.1.0'
    ExpectedClientVersion = '0.1.14'
    FixtureDirectory = $fixtures
    EvidenceDirectory = $evidence
    TimeoutSeconds = 360
}

.\Test-Nov20Surface.ps1 @test -Backend Dx11 -Scale 67 -Audio -ExerciseControls
.\Test-Nov20Surface.ps1 @test -Backend Dx12 -Scale 100 -Audio -ExerciseControls
.\Test-Nov20Surface.ps1 @test -Backend Dx11 -Scale 67 -Hooks NativeOnly
.\Test-Nov20Surface.ps1 @test -Backend Dx12 -Scale 100 -Hooks WasmOnly
.\Test-Nov20Surface.ps1 @test -Backend Dx12 -Scale 67 -AdaptiveResolution
```

Use separate invocations. The harness closes the isolated candidate after each case and rejects an overlapping run. It waits for the requested active mod markers while checking that the disabled marker is absent, so a loading screen alone cannot pass a toggle test. The adaptive case sets a 144 FPS test target and records the stock F3 dynamic-scale line at the original, reduced and restored window sizes. Read those captures before deciding whether the controller changed its selected scale. Inspect the input screenshots for actual movement, inventory and chat. Minimize and restoration exercise app foreground recovery. Physical suspend, touch, controllers and gyro motion require interaction with the device.

The evidence directory receives screenshots and a compact result, along with the game log and a copy of the isolated test settings. The harness updates its isolated package’s settings and saves. It preserves a copied world when it already exists. Use a fresh isolated profile when the original fixture viewpoint is necessary.
