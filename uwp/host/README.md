# UWP host

The CoreWindow host has two build modes. The native-only mode displays a D3D11
clear frame; the game mode links the Rust bridge and runs Horizon. The current
game renderer uses wgpu's GLES backend through ANGLE's D3D11 backend. It has
been run on x64 and ARM64 UWP, but is still experimental: terrain and hands
have been missing on the tested devices, and audio is disabled. ANGLE's GLES 3
path requires a compatible device; it is not a GLES 2 renderer for feature
level 9_3 phones.

First build the Rust game bridge as described in `../game-bridge/README.md`.
Then, from the repository root, set `HORIZON_UWP_GAME_LIB` to the absolute
path of its release static library and build/package the desired architecture:

```powershell
$env:HORIZON_UWP_GAME_LIB = '<absolute path to horizon_uwp_game_bridge.lib>'
cmd /c uwp\host\build-game.cmd x64
if ($LASTEXITCODE -ne 0) { throw 'Game host build failed' }
./uwp/host/package.ps1 -Architecture x64 -Game -RequireAngle
```

Use `arm64` instead of `x64` for the other currently supported game target.
The game build script uses a strict UWP link, checking that its link-only
sentinels for unsupported APIs were not retained. These scripts currently use
the locally installed Visual Studio 18 and Windows SDK 15063 for x64 or SDK
26100 for ARM64; adjust the paths for another machine.

The packager selects locally built UWP ANGLE DLLs from
`.local/vcpkg-x64/installed/x64-uwp-local/bin` (x64) or
`.local/vcpkg/installed/arm64-uwp-local/bin` (ARM64). Alternatively supply a
matching UWP build with `-AngleBin <directory>`. Build inputs and Minecraft
assets are not committed. `-Game` explicitly chooses the game host produced by
`build-game.cmd`, so an old native-only host is not packaged by accident.

Packaging creates an **unsigned** APPX and a loose package directory under
`build`. With Developer Mode enabled, the loose package can be registered with
`Add-AppxPackage -Register <path-to-package/AppxManifest.xml>`. Installing an
APPX requires a certificate trusted by the target. The host uses the matching
architecture of the Microsoft.VCLibs.140.00 framework package. Put Horizon's
prepared resources in the package's `LocalState/resources` directory; game
data and logs are kept separately under `LocalState/game`.

To build the native-only presentation probe, run
`cmd /c uwp\host\build-native.cmd x64` followed by
`./uwp/host/package.ps1 -Architecture x64`. This probe also supports `arm64`
and `arm`, but the ARM32 host alone is not the Horizon game.
