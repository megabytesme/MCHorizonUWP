# UWP host

The CoreWindow host can run a native graphics probe or the Horizon game bridge.
The game supports the experimental native D3D11 renderer, including the
phone-tested ARM32 path. Other renderers have separate compatibility limits;
the native path does not require ANGLE.

Build the Rust bridge with the desired features using the patched Rust source,
then set `HORIZON_UWP_GAME_LIB` to its static library and run
`cmd /c uwp\host\build-game.cmd <x64|arm|arm64>`. Enable host adapters with
`HORIZON_UWP_XAUDIO=1` and `HORIZON_UWP_HTTP=1` when the matching Rust features
are enabled. See `../xaudio-stream/README.md` and `../https-bridge/README.md`.
The host uses a strict UWP link and the locally installed Visual Studio and SDK
paths in the build script. The game host reserves an 8 MiB thread stack, and
the link checks the resulting PE header: Horizon's startup can overflow the
default 1 MiB UI-thread stack before entering multiplayer. This is reserved
address space; Windows commits stack pages as they are needed.

Run `./uwp/host/package.ps1 -Architecture x64 -Game -Resources <prepared-dir>`
with a fresh `-OutputDirectory` to package prepared resources. `-Executable`
can select an explicit host; ANGLE is optional unless `-RequireAngle` is set.
Packages are unsigned and require matching VCLibs and a certificate trusted by
the target. Game data lives in `LocalState/game`. A local `LocalState/resources`
tree takes precedence over bundled resources. Do not overwrite existing state
when installing or upgrading a package.

For the native-only probe, run `cmd /c uwp\host\build-native.cmd x64` and
package without `-Game`. Compilation and strict linking do not establish that a
package works on a particular device.
