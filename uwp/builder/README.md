# Horizon UWP Builder preview

This desktop WinUI 3 preview checks a small set of build prerequisites and
records a target, renderer build, and optional native touch and device-vibration
source-mod selections.
The latter has its own in-game setting, separate from controller vibration.
The touch selection maps to the `native-touch-mod` Cargo feature and includes
touch-look sensitivity and on-screen control-size sliders, plus optional
gyroscope aiming when the device exposes a Windows gyrometer. Gyro aiming is
off by default and is separate from controller input. The touch mod has been
tested on a Lumia in its separate branch. The local recipe builds from a pinned source
snapshot and links to official setup instructions for
missing tools. On request it can invoke the system Windows Package Manager for
fixed Rustup, Git and Python packages, with interactive license/elevation prompts and normal
package verification. The local recipe copies user-selected prepared resources
into private package output, but does not sign or install packages. Tool presence does not establish version or recipe
compatibility with every machine; the x64 and ARM32 recipes have been built locally. Visual Studio detection checks common installation
locations and may miss custom locations.

Build on Windows with the .NET 8 SDK, Windows App SDK NuGet packages and Visual
Studio's AppxPackage build tasks (the script looks in standard install locations):

```powershell
.\build.ps1
.\build.ps1 -Publish
dotnet run --project .\Tests\Horizon.Builder.Tests.csproj -c Release
dotnet run --project .\Tests\Horizon.Builder.Tests.csproj -c Release -- --scan
dotnet run --project .\Tests\Horizon.Builder.Tests.csproj -c Release -- --framework-probe
```

The normal build bundles the Windows App SDK but requires the .NET 8 runtime.
`-Publish` bundles both runtimes. One self-contained x64 publish was 164.9 MiB
and its process started in a clean Windows Sandbox without .NET installed;
interactive controls and clean-machine tool installation remain untested. See
Microsoft's
[unpackaged WinUI deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app).
This is a development build, not a tested clean-machine installer. The full
plan and unresolved device and licensing requirements are in
[BUILDER-DESIGN.md](../BUILDER-DESIGN.md).

The core includes a canonical input record with hashes for the recipe, source
revision, lockfile, patched Rust standard library, resource inventory and
selected native mods, plus toolchain versions. It contains no file paths or keys.
This is groundwork only: callers must verify each input, and the current list
is not a complete reproducibility guarantee or a verified recipe.

The local plan uses pinned source commit
`e9bfe852fbc1ad1d31e6eb22f44972a740774162`, Rust 1.99.1 and Minecraft
26.3 resources. It checks that the selected Git repository contains that
revision, validates the prepared-resource manifest version, and reports the
exact tool locations required by that revision's host/package scripts. The
port is based on Horizon nightly `2026-11-03` at upstream source commit
`f42ef42ef6ceccb3c6a20bea2481d87949a51c81`; builds embed the upstream nightly
date and short commit in Horizon's version fields. They are UWP port builds,
not the official Horizon binaries. The upstream nightly's publication version
is `2026.1103.930.50`; the current default UWP APPX version is
`2026.1103.930.58`. Keep the same package identity for upgrades and choose a
higher version when rebuilding the port for the same nightly. This
recipe is pinned; a newer upstream nightly must be integrated and verified
before the builder's pin is updated. On startup the builder checks GitHub's
public nightly release metadata and reports when its source differs; a failed
check does not prevent an offline build from the pinned source. The check
does not download or execute the new release. Minecraft resource framework versions
remain tied to the Minecraft resources, not to the Horizon nightly date.
The x64 client includes native D3D11 and WGPU's DX12 backend; it also includes
ANGLE when the matching x64 UWP DLLs are available in the selected checkout.
The ARM32 client includes native D3D11 only. Runtime device and surface probes
show only renderers that the installed package can use on that device. DX12
and ANGLE are package inputs on x64; the ARM32 DX12 build fails in the upstream
Windows bindings before linking, and this recipe has no ARM32 ANGLE runtime.
These are build-input constraints, not assumptions about the device GPU. The
native D3D11 pre-game check creates a feature-level 9_1 device while XAML owns
the view; the game checks its CoreWindow presentation surface after the shell
detaches its page. This avoids treating XAML's active compositor as a failed
game swap chain. On Windows 10 Mobile, input handlers
are registered before the XAML page is detached; the game receives its initial
size after the handoff. The pre-game page and gameplay share one CoreWindow on
every target, including desktop and Windows 10 Mobile. The preview runs x64
and ARM32 plans in fresh builder-owned folders under
`%USERPROFILE%\.huwp\b`. The selected checkout is never modified. The
Cargo fetches missing dependencies for the selected target and x64 build host
from configured registries before the offline build. It can also create a reusable
resource framework package for the selected prepared resources. Parallel Rust
compilation is capped to roughly one job per 4 GiB of runtime memory budget and
the available logical processors, to reduce compiler out-of-memory failures. Signing and
installation are not yet integrated. A build requires
at least 10 GiB free on the workspace drive. ARM32 uses the existing offline
dependency override; account, skin and Minecraft session requests use WinRT HTTPS.
The Microsoft application (client) ID is public and is compiled into the client.
At the user’s request the UI defaults to the [Fluent Launcher public client ID](https://github.com/Xcube-Studio/Natsurainko.FluentLauncher/blob/main/Natsurainko.FluentLauncher/Services/Accounts/AuthenticationService.cs);
Microsoft accepted a device-code request for it. Minecraft/Xbox authorization with
that ID has not yet been verified. The ID can be cleared for offline play or changed
to a separately approved ID.
Register a public client supporting personal Microsoft accounts and device-code flow;
New IDs require [Minecraft Java App ID review](https://aka.ms/mce-reviewappid)
for Minecraft services, as described in [Horizon’s account documentation](../../docs/microsoft-account.md). Without one, offline
play remains available. The native pre-game page uses Credential Locker, shows a device
code to enter in a browser on another device, and asks before replacing or removing
a saved account. The UWP client uses Windows service discovery for Minecraft SRV
records, and its built-in native picker imports content mods and resource-pack ZIPs.
Real-account and physical-device validation remain outstanding.
The output uses the prototype publisher identity.
The combined recipe has not yet been validated on a phone. The builder warns
when `assets/minecraft/sounds.json` or the sounds directory is missing; this
is only a presence check, not a complete sound-integrity check. To prepare
missing sounds, select your Minecraft installation and enable sound preparation.
The builder copies the chosen resources into its private workspace, builds
`horizon-prepare` from the pinned source and uses it to verify and copy sound
objects. It does not modify the selected resource tree or Minecraft install.
If the selected resources already contain any sound data, disable sound
preparation or choose a sound-free copy; existing data is not overwritten.
The package-version field changes only the isolated manifest and cannot be
lower than the pinned source version. Choose a version newer than the package
with the same identity already installed on the target; the builder cannot
yet inspect that device or guarantee an upgrade preserves data.

The framework probe uses the installed Windows SDK MakeAppx to create unsigned
x64 and ARM framework packages containing only a synthetic marker in new
temporary directories. It validates their manifests and content and never
reads Minecraft resources or signs packages. The separate x64 Sandbox package
dependency probe established that a client can discover and read an installed
synthetic framework. ARM32 framework installation and dependency access on
Windows 10 Mobile remain untested.
