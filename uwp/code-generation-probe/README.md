# UWP executable memory and Wasmtime probes

The executable-memory test creates two signed APPXs with distinct test identities and identical executables. The enabled package declares the general-use codeGeneration capability. The control omits it. Both allocate writable anonymous memory, copy a fixed function returning 42, change the page to read-execute, flush the instruction cache, call the function, and release the page. Each writes result.json under its own LocalState and exits. The manifest minimum is build 16299. SDK 15063's ARM Store headers omit FlushInstructionCache.

Run the scripts from this repository in a terminal with the repository's MSVC Store toolsets and Windows SDKs. Use an authorized code-signing certificate from CurrentUser My. The packages need the architecture-matched VCLibs UWP framework and a user session that trusts the test certificate.

~~~powershell
$probe = 'uwp/code-generation-probe'
& "$probe/build.ps1" -Architecture x64 -Output $memoryProbeOutput -SigningThumbprint $testCertificate.Thumbprint
~~~

The build accepts x64, arm64 and arm. The runtime test succeeded on x64 Windows build 26300, where the enabled app returned 42 and the control reported Win32 5 at the RX transition. ARM64 and ARM32 were strict-linked and signed. The ARM32 experiment needs a Windows build providing the 16299 API contract.

## Wasmtime 50.0.2 with Pulley

The separate x64 test compiles a fixture module in AppContainer using Cranelift's Pulley target and executes it through Wasmtime. It calls a Wasm export repeatedly, enforces the configured linear memory limit, checks the memory-out-of-bounds trap and exhausts fuel on an infinite loop. The package uses no executable-memory capability. The experiment uses the exact Wasmtime 50.0.2 registry source, a patched copy of Rust 1.99.1 std, cached Cargo dependencies and the verified Nightly x64 host kit.

~~~powershell
& "$probe/wasmtime/build.ps1" -WasmtimeSource $wasmtime50_0_2 -HostKit $x64HostKit -Output $runtimeOutput
& "$probe/build.ps1" -Architecture x64 -Output $wasmtimeProbeOutput -SigningThumbprint $testCertificate.Thumbprint -WasmtimeDll "$runtimeOutput/horizon_wasmtime_uwp_probe.dll" -WasmtimeImportLibrary "$runtimeOutput/horizon_wasmtime_uwp_probe.lib"
~~~

Install and activate each test APPX in the interactive test account. Read result.json from the installed package family's LocalState. For Pulley, a status of pass with value 0 means all checks passed. Nonzero values identify the failing assertion in the probe's Rust source.

The Windows memory patch uses UWP anonymous reservation and protection, and copies file-backed inputs at the original logical length. The crate disables native backend and native signal registration on UWP. The patch leaves Wasmtime's other targets on their existing paths. Real Horizon mod execution requires the generated ABI and a validated game worker stack. The package exercises the standalone interpreter, and the product game's runtime settings are independent. [Development report](../../docs/nightly-development-2026-11-19.md)
