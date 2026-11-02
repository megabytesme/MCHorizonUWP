# Horizon UWP Builder preview

This desktop WinUI 3 preview checks a small set of build prerequisites and
records a target and optional native touch source-mod selection. The touch
selection maps to the `native-touch-mod` Cargo feature. The mod has been tested
on a Lumia in its separate branch. The local recipe builds from a pinned source
snapshot and links to official setup instructions for
missing tools. On request it can invoke the system Windows Package Manager for
fixed Rustup, Git and Python packages, with interactive license/elevation prompts and normal
package verification. The local recipe copies user-selected prepared resources
into private package output, but does not sign or install packages. Tool presence does not establish version or recipe
compatibility; the required UWP Rust patches and build recipes still need a
verified implementation. Visual Studio detection checks common installation
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
selected touch mod, plus toolchain versions. It contains no file paths or keys.
This is groundwork only: callers must verify each input, and the current list
is not a complete reproducibility guarantee or a verified recipe.

The local plan uses pinned source commit
`5a103f769c8fe00ec38844003ad18b7878e24dfc`, Rust 1.99.1 and Minecraft
26.3 resources. It checks that the selected Git repository contains that
revision, validates the prepared-resource manifest version, and reports the
exact tool locations required by that revision's host/package scripts. The
preview runs x64 and ARM32 plans in fresh builder-owned folders under
`%USERPROFILE%\.huwp\b`. The selected checkout is never modified. The
Cargo build uses the existing offline cache; dependency acquisition, signing,
framework packaging and installation are not yet integrated. A build requires
at least 10 GiB free on the workspace drive. ARM32 uses the existing offline
override and has no HTTPS; the output uses the prototype publisher identity.
One ARM32 native-touch package was strictly linked and packaged from this
runner, but that specific output has not been installed or tested on a phone.

The framework probe uses the installed Windows SDK MakeAppx to create unsigned
x64 and ARM framework packages containing only a synthetic marker in new
temporary directories. It validates their manifests and content and never
reads Minecraft resources or signs packages. The separate x64 Sandbox package
dependency probe established that a client can discover and read an installed
synthetic framework. ARM32 framework installation and dependency access on
Windows 10 Mobile remain untested.
