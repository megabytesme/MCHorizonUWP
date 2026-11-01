$ErrorActionPreference = 'Stop'
if (-not $env:HORIZON_PANORAMA_PROBE_LIB) { throw 'Set HORIZON_PANORAMA_PROBE_LIB to the UWP Rust static library.' }
& (Join-Path $PSScriptRoot '..\..\rust-std\strict-link.ps1') -Objects @('App.obj') `
    -RustLibrary $env:HORIZON_PANORAMA_PROBE_LIB -Machine X64 `
    -Output (Join-Path $PSScriptRoot 'build\HorizonUWP.exe')
