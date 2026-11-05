# MCHorizonUWP

An unofficial UWP port of [Horizon](https://github.com/horizon-rust/horizon),
with a WinUI 3 desktop builder. This repository is under development: the
builder cannot yet provision a fresh PC, authenticate and prepare all game
resources, sign its build output, install native mods, or complete an install.
The preview can browse Horizon's native-mod index, but does not establish UWP
compatibility or include those mods in a build. Do not
treat the current preview as a supported release.

The intended distribution separates three independently versioned components:
the desktop builder, reusable UWP client source, and a patch for an exact
Horizon revision. A compatibility catalog will select known-compatible
combinations and report evidence separately for each target architecture.
See [the catalog design](docs/catalog.md).

The first candidate patch is in `patches/nightly-2026-11-03/`; it targets
Horizon commit `f42ef42ef6ceccb3c6a20bea2481d87949a51c81`. The patch has
been applied to an archive of that revision and compared against the port's
changed files with Windows line endings normalized. This is not an end-to-end
build or runtime verification.

The builder preview lives in `uwp/builder`. On a development machine with
.NET 8, the Windows App SDK build dependencies and Visual Studio AppxPackage
tasks installed, run `uwp/builder/build.ps1 -Publish` and
`dotnet run --project uwp/builder/Tests/Horizon.Builder.Tests.csproj -c Release`.
The published x64 wizard has been opened and navigated in Windows Sandbox;
the clean-machine build flow remains incomplete.

Minecraft game files, assets, prepared resources, accounts, signing keys and
user saves must never be committed. Horizon's code is MIT-licensed; preserve
its copyright and the included third-party notices when distributing it.
