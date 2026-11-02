# Horizon UWP Builder preview

This desktop WinUI 3 preview checks a small set of build prerequisites and
records a target and optional native touch source-mod selection. The touch
selection maps to the planned `native-touch-mod` Cargo feature; the feature and
its recipe must be verified before the builder can run a build. It does
not download or run installers, access Minecraft files, configure signing, build
Horizon, or create packages. Tool presence does not establish version or recipe
compatibility; the required UWP Rust patches and build recipes still need a
verified implementation. Visual Studio detection checks common installation
locations and may miss custom locations.

Build on Windows with the .NET 8 SDK, Windows App SDK NuGet packages and Visual
Studio's AppxPackage build tasks (the script looks in standard install locations):

```powershell
.uild.ps1
dotnet run --project .\Tests\Horizon.Builder.Tests.csproj -c Release
dotnet run --project .\Tests\Horizon.Builder.Tests.csproj -c Release -- --scan
```

The app targets an unpackaged deployment with the Windows App SDK runtime
bundled. This build still requires the .NET 8 runtime; a fully self-contained
publish and clean-machine installation remain unverified. See Microsoft's
[unpackaged WinUI deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app).
This is a development build, not a tested clean-machine installer. The full
plan and unresolved device and licensing requirements are in
[BUILDER-DESIGN.md](../BUILDER-DESIGN.md).

The core includes a canonical input record with hashes for the recipe, source
revision, lockfile, patched Rust standard library, resource inventory and
selected touch mod, plus toolchain versions. It contains no file paths or keys.
This is groundwork only: callers must verify each input, and the current list
is not a complete reproducibility guarantee or a verified recipe.
