# MCHorizonUWP

**MCHorizonUWP** is an unofficial UWP port of [Horizon](https://github.com/horizon-rust/horizon), a Minecraft: Java Edition client written in Rust. The project includes an **MCHorizonUWP Builder (WinUI 3)** for preparing and packaging Horizon for Windows devices that support UWP apps.

**Disclaimer:** This is an unofficial project. It is not affiliated with or endorsed by Mojang Studios or Microsoft. Minecraft and its related names are trademarks of their respective owners.

## Features

* **Four architectures:** Choose x86, x64, ARM32 or ARM64. **MCHorizonUWP Builder** selects the architecture of the current device by default.
* **Broad renderer support:** Choose between DirectX 11, DirectX 12 and ANGLE (ES 3.0).
* **Controller support:** Play using your favourite controllers.
* **Horizon releases:** Select a Horizon patch and UWP client from the signed compatibility catalog. The builder checks the selected versions and target before building.
* **Native Horizon mods:** Browse the Horizon native-mod registry and choose which mods to include. Compatibility depends on the patch and each mod; unsupported combinations are rejected by the build recipe.
* **Minecraft resources:** Sign in with a Microsoft account that owns Minecraft: Java Edition. After ownership is confirmed, the builder prepares Minecraft 26.3 resources and can create a separate resource framework or include resources with the client.
* **Signed packages:** The builder signs the client and any new resource framework with a local certificate. The output includes the public certificate and an installation guide.
* **Automatic setup:** The builder checks for existing tools and SDK files, then downloads compact pinned packages when needed. Setup progress and the applicable license terms are shown in the UI. Some Microsoft frameworks may require Windows elevation.
* **Automatic renderer selection:** The UWP client selects an available renderer on the target device.

## Supported Architectures

These are examples of devices that use each architecture. They do not mean MCHorizonUWP has been tested or confirmed to work on those devices.

| Architecture    | Examples                                                                                                                                                |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **x64**   | Intel and AMD PCs, Intel Surface Pro models, Xbox One and Xbox Series consoles, Surface Neo prototype                                                   |
| **ARM64** | Snapdragon and Microsoft SQ PCs, Surface Pro X, Surface Pro 9 with 5G, Surface Pro 11, HoloLens 2, Surface Duo running a community Windows-on-ARM build |
| **ARM32** | Windows 10 Mobile phones such as Lumia 950 and Lumia 950 XL                                                                                             |
| **x86**   | 32-bit Windows PCs and the first-generation HoloLens                                                                                                    |

## Download

See the [GitHub releases](https://github.com/megabytesme/MCHorizonUWP/releases) for development builds. The current releases are previews; check the status above before using them.

## Using the Builder

When a compatible client release is available, the wizard guides you through the build:

1. Download and extract the builder, then run `Horizon.Builder.App.exe`.
2. Choose the target architecture. The builder uses the current PC architecture as the default.
3. Sign in with the Microsoft account that owns Minecraft: Java Edition.
4. Choose a compatible Horizon patch and UWP client, then select any native mods.
5. Choose whether to create a resource framework or include Minecraft resources with the client. Set the package version and review the build summary.
6. Let the builder prepare missing dependencies, build the selected packages and sign them.
7. Install the package on the current PC when supported, or follow the included guide to install it on another device.

The current signed catalog blocks selection at step 4 until a compatible client release is available. Minecraft resources are prepared locally after ownership is confirmed. Keep resource-containing packages private.

## Releases

The builder, UWP client and Horizon patch are versioned separately. A new Horizon nightly can receive a patch without copying the client into it, and a client update does not need to duplicate the patch. The signed catalog declares which versions and architectures can be built together; it records build and device evidence separately. See the [compatibility catalog](docs/catalog.md) for details.

* **Horizon patches** contain UWP-specific changes applied to one exact Horizon revision.
* **UWP client releases** contain the Windows client source and the prebuilt host files used by the builder.
* **Builder releases** contain the self-contained WinUI 3 desktop application.

## Build Guide

### Building the builder

Building the WinUI 3 builder from source requires Windows, the .NET 8 SDK, Windows App SDK packages and the Visual Studio AppxPackage build tasks. From `uwp/builder`, run:

```powershell
.\build.ps1 -Configuration Release -Publish
dotnet run --project .\Tests\Horizon.Builder.Tests.csproj -c Release
```

The published builder is a self-contained x64 application. For setup details and current validation results, see the [builder guide](uwp/builder/README.md) and [fresh-PC validation notes](uwp/builder/CLEAN-PC-VALIDATION.md).

### Building a client package

The builder checks local tools first and prepares its own compact dependencies when they are missing. A Microsoft account that owns Minecraft: Java Edition is required before resource preparation starts. On a fresh Windows PC, the current dependency resolver completed the four architecture setups in about 5 minutes 46 seconds; building and signing all four client APPXs from a fresh PC has not yet passed.

## Contributing

Builder screens and dependency setup are in `uwp/builder/`. UWP client code is in `uwp/`. Horizon-specific changes belong in a patch against the exact upstream revision. See [the catalog guide](docs/catalog.md) for how the releases fit together.

Please don't commit Minecraft game files, assets, prepared resources, account tokens, signing keys or player saves. Contributions and bug reports are welcome through [GitHub Issues](https://github.com/megabytesme/MCHorizonUWP/issues).

## License

Horizon is licensed under the MIT License; see [LICENSE](LICENSE). Preserve the copyright notices and read [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) before distributing the builder or client.
