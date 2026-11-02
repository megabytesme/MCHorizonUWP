# Horizon UWP Builder preview

This desktop WinUI 3 preview checks a small set of build prerequisites and
records a target and optional native touch source-mod selection. The touch
selection maps to the `native-touch-mod` Cargo feature. The mod has been tested
on a Lumia in its separate branch; this preview still needs an integrated
recipe before it can run a build. It links to official setup instructions for
missing tools. It does not download or run installers, access Minecraft files,
configure signing, build
Horizon, or create packages. Tool presence does not establish version or recipe
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
