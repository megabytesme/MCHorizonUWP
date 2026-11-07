# Third-party notices

## ZstdSharp.Port

The MCHorizonUWP Builder bundles `ZstdSharp.Port` 0.8.8 by Oleg Stepanischev,
at upstream commit `2cd0c019693bc786a5fe5c3be94e107b24e7267e`, under the MIT
license. The package is used to read the pinned LLVM Zstandard archive. Its
source is available at https://github.com/oleg-st/ZstdSharp.

> Copyright (c) 2026 Oleg Stepanischev
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## ANGLE and zlib

Packages that include ANGLE and its zlib runtime also include their license
notices in `licenses/ANGLE.txt` and `licenses/zlib.txt`, respectively. Packages
without those runtime libraries do not include these additional notices.

## USB ID Repository

`third_party/usb-ids/usb.ids` is the 2026-06-26 snapshot of the
[USB ID Repository](https://usb-ids.gowdy.us/), maintained by Stephen J. Gowdy
and contributors. The database is distributed under the BSD-3-Clause option
(`third_party/usb-ids/LICENSE`); the upstream repository offers GPL-2.0-or-later
as an alternative. Names describe reported IDs, not verified product brands.

## SDL Game Controller Database

The `gilrs` 0.11.2 dependency embeds [SDL_GameControllerDB](https://github.com/mdqinc/SDL_GameControllerDB),
Copyright (C) 1997-2025 Sam Lantinga, under the zlib license
(`third_party/controller-mappings/LICENSE`).

## gilrs-core

`third_party/gilrs-core` is [gilrs-core](https://gitlab.com/gilrs-project/gilrs)
0.6.8 at commit `07e286e24b046cf39e5c367daa2770b805a64692`, used under the
MIT license (`LICENSE-MIT` in that directory). Its Windows backend uses a
connection-scoped identity when older Windows does not provide NonRoamableId.

## parking_lot_core

`third_party/parking_lot_core` is [parking_lot_core](https://github.com/Amanieu/parking_lot)
0.9.12 at commit `d7828fff7b5d6327ae608e82db45f888b344449a`, licensed under MIT
or Apache-2.0 (`LICENSE-MIT` and `LICENSE-APACHE` in that directory). The copy
binds the UWP address-wait APIs directly instead of looking up desktop modules.

## winit

`third_party/winit` is [winit](https://github.com/rust-windowing/winit) 0.30.13 at
commit `e9809ef54b18499bb4f2cac945719ecc2a61061b`, licensed under Apache-2.0
(`third_party/winit/LICENSE`). This copy adds an experimental UWP CoreWindow backend
and changes platform selection for UWP targets.

## wgpu-hal

`third_party/wgpu-hal` is [wgpu-hal](https://github.com/gfx-rs/wgpu) 30.0.1 by the gfx-rs
developers, with upstream fixes applied
([gfx-rs/wgpu#10124](https://github.com/gfx-rs/wgpu/pull/10124),
[gfx-rs/wgpu#10714](https://github.com/gfx-rs/wgpu/pull/10714)). It is licensed under MIT or
Apache-2.0; both licenses are in that folder (`LICENSE.MIT`, `LICENSE.APACHE`).

## naga

`third_party/naga` is [naga](https://github.com/gfx-rs/wgpu) 30.0.1 by the gfx-rs developers, with
fixes to its GLSL frontend that shader packs need, each applied in the commit that needed it. It is licensed under MIT or Apache-2.0; both licenses
are in that folder (`LICENSE.MIT`, `LICENSE.APACHE`).

## libmimalloc-sys and mimalloc

`third_party/libmimalloc-sys` is [libmimalloc-sys](https://github.com/purpleprotocol/mimalloc_rust)
0.1.49 by Octavian Oncescu, with the [mimalloc](https://github.com/microsoft/mimalloc) v3 sources it
bundles (Copyright (c) 2018-2025 Microsoft Corporation, Daan Leijen), both under the MIT license
(`LICENSE.txt` and `c_src/mimalloc/v3/LICENSE` in that folder). It holds only the v3 sources. One
change: `mi_popcount` in `include/mimalloc/bits.h` uses mimalloc's portable bit count on MSVC
x86/x64 instead of the POPCNT instruction, which CPUs before Nehalem lack (#7264).

## Pixelify Sans

`crates/horizon-theme/fonts/PixelifySans-*.ttf` is [Pixelify Sans](https://github.com/eifetx/Pixelify-Sans)
by The Pixelify Sans Project Authors, licensed under the SIL Open Font License 1.1
(`crates/horizon-theme/fonts/OFL-PixelifySans.txt`).

## Pixelarticons

`crates/horizon-theme/icons/pixel/*.svg` are icons from
[Pixelarticons](https://github.com/halfmage/pixelarticons) by Gerrit Halfmann, under the MIT
license (`crates/horizon-theme/icons/pixel/LICENSE-pixelarticons.txt`). `star.svg` is Horizon's own, drawn on
the same grid. The UWP `WorldPlaceholder.png` and `ServerPlaceholder.png`
are rendered and recolored from the Pixelarticons globe and server shapes.

## Hugeicons

`crates/horizon-theme/icons/glass/*.svg` are icons from the free set of
[Hugeicons](https://github.com/hugeicons/hugeicons) (`@hugeicons/core-free-icons` 4.4.0), under
the MIT license (`crates/horizon-theme/icons/glass/LICENSE-hugeicons.txt`), exported with a 1.6
stroke in `currentColor`.

`crates/horizon-theme/icons/glass/discord.svg` is Discord's logo as drawn by
[Simple Icons](https://github.com/simple-icons/simple-icons) 13.21.0 (CC0 1.0). The logo is Discord's
trademark, used only on the button that opens Horizon's Discord server.

## AMD FidelityFX SDK (FSR 3.1 frame generation)

`crates/horizon-client-render/src/gfx/frame_gen/optical_flow.wgsl` and `interpolation.wgsl` are
ports to WGSL of the optical flow and frame interpolation shaders of the
[AMD FidelityFX SDK](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK) 1.1.4
(`sdk/include/FidelityFX/gpu/{opticalflow,frameinterpolation}`, at commit
[c6efa6bf7f20](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/tree/c6efa6bf7f2027b3ec94f28578bb5965eabb9e55)),
and `optical_flow.rs` follows the SDK's `sdk/src/components/opticalflow/ffx_opticalflow.cpp`
(its passes, their order and their dispatch sizes). Copyright (C) 2024 Advanced Micro Devices,
Inc., under the MIT license, whose text heads each file. AMD and FidelityFX are AMD's
trademarks; Horizon is not affiliated with AMD.

## Mod loader logos

The launcher shows the logo of the mod loader an instance plays with, and offers the logos as
instance icons.

- `launcher/assets/logos/{fabric,quilt,neoforge,forge}.png` are the loaders' logos as the launcher
  mockup on [playhorizon.dev/launcher](https://playhorizon.dev/launcher) shows them (horizon-site
  `public/media/launcher/*.webp`, converted to PNG), each its project's own mark:
  [Fabric](https://fabricmc.net/), [Quilt](https://quiltmc.org/) (its art is
  [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)),
  [NeoForged](https://neoforged.net/) (its art, by Ridanisaurus, is
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)) and
  [Minecraft Forge](https://minecraftforge.net/). They name the loader an instance runs.

Each logo belongs to its project. Showing it says which loader an instance runs; it does not mean
the project endorses Horizon.

## Sign in with Microsoft

The launcher's sign-in buttons follow Microsoft's published branding guidelines for apps that sign in
with Microsoft accounts
([Sign in with Microsoft branding](https://learn.microsoft.com/en-us/entra/identity-platform/howto-add-branding-in-apps)).
The four-square Microsoft symbol in `launcher/src/microsoft.rs` reproduces the squares, positions and
colours of Microsoft's official `ms-symbollockup_mssymbol_19.svg` from that page, unmodified. Microsoft
and its logo are trademarks of the Microsoft group of companies; Horizon is not endorsed by Microsoft.

## CIT Resewn

`crates/horizon-optifine/src/cit.rs` and `crates/horizon-client-render/src/cit.rs` follow the
condition semantics of [CIT Resewn Continuation](https://github.com/EthanVisagie/CIT-Resewn-Continuation-1.21.11)
(at `b792f9a`), a continuation of [CIT Resewn](https://github.com/SHsuperCM/CITResewn) by SHsuperCM: the
1.21 component paths, enchantment levels, percent damage, text matching and rule ordering. It is
licensed under the MIT license:

> Copyright (c) 2021 SHsuperCM
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software
> and associated documentation files (the "Software"), to deal in the Software without
> restriction, including without limitation the rights to use, copy, modify, merge, publish,
> distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
> Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
> BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## FusionExamples

The tile tables in `crates/horizon-optifine/src/fusion/layout.rs` were read off the connecting
texture templates (`templates/template <layout>.png`) of
[FusionExamples](https://github.com/SuperMartijn642/FusionExamples) (at `8c9c56e`), which is
licensed under the MIT license:

> Copyright (c) 2024 Martijn van den Brand
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software
> and associated documentation files (the "Software"), to deal in the Software without
> restriction, including without limitation the rights to use, copy, modify, merge, publish,
> distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
> Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
> BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## BlueMap

`crates/horizon-client-render/src/bluemap.rs` reads the lowres tiles of a server's
[BlueMap](https://github.com/BlueMap-Minecraft/BlueMap) web map. It follows three of BlueMap's files:
`core/src/main/java/de/bluecolored/bluemap/core/map/lowres/LowresLayer.java` (`saveTile`) for the
tiles' pixel layout, `common/webapp/src/js/util/Utils.js` (`pathFromCoords`) for their paths, and
`core/src/main/java/de/bluecolored/bluemap/core/map/MapSettingsSerializer.java` for a map's
`settings.json`, at commit
[96ae320d1d39](https://github.com/BlueMap-Minecraft/BlueMap/tree/96ae320d1d39581c10058d8222f2290f2fcb5333).
BlueMap is licensed under the MIT license:

> Copyright (c) Blue (Lukas Rieger) <https://bluecolored.de>
> Copyright (c) contributors
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in
> all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
> THE SOFTWARE.
