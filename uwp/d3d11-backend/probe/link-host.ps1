$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '..\..\rust-std\strict-link.ps1') -Objects @('App.obj') `
    -RustLibrary $env:HORIZON_D3D11_PROBE_LIB -Output (Join-Path (Get-Location).Path 'HorizonUWP.exe') `
    -Machine $env:PROBE_MACHINE -ExtraLibraries @('d3d11.lib', 'dxgi.lib')
if (-not $?) { exit 1 }
