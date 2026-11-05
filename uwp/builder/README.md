# Horizon UWP Builder preview

The WinUI 3 builder is an in-development wizard for assembling a Horizon UWP
client package. It selects the current Windows architecture by default and
supports x86, x64, ARM32 and ARM64 targets. The client probes its packaged
renderers and selects one on the target device.

The builder fetches its signed compatibility catalog from this repository,
then verifies the hashes of the selected Horizon source, patch and client
artifacts before assembling them. The first patch and client are published as
prereleases. A catalog compatibility rule is not a successful build or device
test: the wizard labels combinations without recorded build evidence as
unverified. This preview has one pinned recipe; later patch and client versions
need compatible recipes and catalog entries.

You can choose bundled Minecraft resources, a new reusable resource framework,
or an existing framework on the target. Minecraft: Java Edition ownership
sign-in is required before a build. The Microsoft client ID is the same public
ID used by the Horizon launcher and UWP client; the builder keeps its sign-in
tokens in memory. After ownership is confirmed, the development version can
download and prepare resources privately using Horizon's pinned tooling. You can
also choose prepared resources or an existing framework. Do not publish packages
or resource frameworks containing Minecraft files.

The options page can browse Horizon's native-mod registry, select and remove
mods, and apply verified bundles through Horizon's mixin tool. Native mods run
code with the player's permissions. A real Zoom bundle has been transformed
and its x64 UWP game bridge compiled, but other mods and devices have not been
established compatible. The builder refuses unsupported combinations, including
mods requiring different mixin revisions, bundled data or post-build steps.
The built-in touch and vibration options are separate from registry mods.

Packages from the authenticated release recipe are signed with a locally
created certificate. The output includes the public certificate, an installation
guide and the target's Microsoft UI and VC runtime framework packages. The
builder restores the pinned WinUI package automatically when absent. If an SDK
does not contain the target's VC runtime APPX, the builder downloads a pinned
Microsoft redistributable, verifies its signature and extracts the signed APPX.
When the target can run on this PC, the builder offers installation
and explains that trusting its certificate may require administrator approval.
Other targets use the included guide. For an existing resource framework, its
exact identity and publisher must match the client; resource
frameworks contain private Minecraft files and should not be published.

When you continue from Welcome, the development builder checks its requirements
and offers to install missing components in the background. The Dependencies
panel shows setup progress while you continue through the wizard. The builder
uses WinGet for Git, Python, Rustup, Clang and Visual Studio 2022 Build Tools;
it also prepares the UWP SDKs and Microsoft framework packages for all four
targets. A new Rustup installation uses a private builder-owned home; the pinned
compiler and rust-src are selected without changing an existing default
toolchain. This setup is under Sandbox validation. Administrator approval may be needed when
installing development tools or trusting the locally generated certificate.
A complete build from a clean PC has not passed. See
[CLEAN-PC-VALIDATION.md](CLEAN-PC-VALIDATION.md) for tested paths and limitations.

## Building the builder

On a development PC with the .NET 8 SDK, Windows App SDK NuGet packages and
Visual Studio AppxPackage build tasks:

```powershell
.\build.ps1 -Configuration Release -Publish
dotnet run --project .\Tests\Horizon.Builder.Tests.csproj -c Release
```

`-Publish` produces a self-contained x64 builder. Its WinUI application opens
in Windows Sandbox without a separate .NET runtime. A native ARM64 or x86
builder distribution has not been established. Builds use private workspaces
under `%USERPROFILE%\.huwp\b` and parallel jobs limited by available memory. At least
20 GiB of free space is required for automatic resource preparation; actual
requirements can be higher.
See [installation guidance](../../docs/install.md) and
[builder design](../BUILDER-DESIGN.md) for further details.
