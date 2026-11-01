$ErrorActionPreference = 'Stop'
if (-not $env:HORIZON_ENTITY_PROBE_LIB) { throw 'Set HORIZON_ENTITY_PROBE_LIB to the UWP Rust static library.' }
& (Join-Path $PSScriptRoot '..\..\rust-std\strict-link.ps1') -Objects @('App.obj') `
    -RustLibrary $env:HORIZON_ENTITY_PROBE_LIB -Machine X64 `
    -Output (Join-Path $PSScriptRoot 'build\HorizonUWP.exe')
if (Select-String -Path (Join-Path $PSScriptRoot 'build\HorizonUWP.exe.map') -Pattern 'horizon_uwp_(get_metrics|set_cursor_visible|set_cursor_position|window_handle|set_ime_allowed)' -Quiet) {
    Remove-Item -LiteralPath (Join-Path $PSScriptRoot 'build\HorizonUWP.exe') -Force
    throw 'A diagnostic window stub survived linking.'
}
