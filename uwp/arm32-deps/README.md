# ARM32 UWP Rust dependencies

The game bridge can be compiled for `thumbv7a-uwp-windows-msvc` with the UWP
standard-library source prepared by `uwp/rust-std/prepare-source.ps1` and
these local generated-binding corrections. This is a compilation milestone,
not a linkable or playable phone build: the current TLS dependency `ring`
does not provide Windows ARM32 assembly, and the existing wgpu renderer does
not support the phone's D3D feature level.

`prepare.py` downloads the locked `windows-sys` versions if necessary and
makes local copies under `.local/arm32-windows-sys`. It extends only the
existing 32-bit layouts of WSADATA, SERVENT, DELAYLOAD_INFO and
MEMORY_BASIC_INFORMATION to `target_arch = "arm"`; it leaves the Cargo
registry unmodified and rejects unexpected generated definitions. The
patches are local overrides, not published forks.

From the repository root in a PowerShell session, after setting up the
matching Rust source and Windows 10 SDK 15063 or newer:

```powershell
python uwp/arm32-deps/prepare.py
$env:RUSTC_BOOTSTRAP = '1'
$env:__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS = 'nightly'
$env:__CARGO_TESTS_ONLY_SRC_ROOT = '<prepared-rust-source>/library'
$env:CC_thumbv7a_uwp_windows_msvc = 'C:\Program Files\LLVM\bin\clang.exe'
$env:CFLAGS_thumbv7a_uwp_windows_msvc = '-DWINAPI_FAMILY=WINAPI_FAMILY_APP'
```

Enter an ARM UWP MSVC developer environment. Then run the following commands;
Cargo uses all available cores unless configured otherwise:

```powershell
cargo +stable --config .local/arm32-windows-sys/patches.toml check --manifest-path uwp/arm32-deps/abi-check/Cargo.toml -Z build-std=std,panic_abort --target thumbv7a-uwp-windows-msvc --release
cargo +stable --config .local/arm32-windows-sys/patches.toml build --manifest-path uwp/game-bridge/Cargo.toml -Z build-std=std,panic_abort --target thumbv7a-uwp-windows-msvc --release
```

To verify the matching layouts directly against SDK headers in that same
ARM developer environment:

```powershell
cl /nologo /c /std:c++17 /MD /DWINAPI_FAMILY=WINAPI_FAMILY_APP /D_WIN32_WINNT=0x0A00 /Fo.local/arm32-sdk-layout.obj uwp/arm32-deps/abi-check/sdk-layout.cpp
```

The Rust ABI assertions and the C++ assertions both pass with SDK 15063.
Cargo may update the bridge lockfile to describe the local path overrides;
restore that local-only change before committing if no dependency versions
have intentionally changed. Passing the checks and building the archive does
not demonstrate that the complete application links or runs. The full game
currently fails strict ARM32 linking because `ring` 0.17.14 does not list a
Windows ARM32 assembly target; resolving secure HTTPS and implementing the
native renderer remain separate work.

For experimental offline ARM32 linker and single-player work only, generate
the separate override and pass it instead of `patches.toml`:

```powershell
python uwp/arm32-deps/prepare.py --offline-arm32
cargo +stable --config .local/arm32-windows-sys/patches-offline-arm32.toml build --manifest-path uwp/game-bridge/Cargo.toml -Z build-std=std,panic_abort --target thumbv7a-uwp-windows-msvc --release
```

This explicitly selected override uses ureq's existing no-TLS behavior:
HTTPS requests return an error, so resource downloads, account and other
HTTPS services cannot work. It cannot be compiled for another architecture or
non-UWP target. It is intended for further linker and offline game development,
not a security workaround or a release configuration. Plain HTTP functionality
is unchanged, and nothing is transparently downgraded from HTTPS to HTTP.

With the offline override, an ARM32 game bridge archive and the existing game
host have also passed the strict SDK 15063 link audit without retained
prohibited API sentinels. This establishes link compatibility only; the
existing wgpu rendering path still cannot render on a feature-level 9_1 phone.
The native D3D11 renderer remains under development.

For 32-bit UWP and ARM64 UWP builds using the native D3D11 feature, first-run
defaults are render distance 4 and simulation distance 5, and automatic chunk
workers are capped at 2 to reduce memory pressure. Existing saved options and
explicitly chosen worker counts or launch arguments take precedence. Other
platforms, including ARM64 UWP builds using wgpu, retain their existing defaults.
