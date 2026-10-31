# ARM UWP prototype builds

These packages contain a UWP CoreWindow and D3D11 clear-color shell linked to a small Rust bridge calling `horizon-core`. They do **not** contain the Horizon game or a port of its renderer. The ARM32 manifest declares minimum Windows version 10.0.15063.0; the package has not been installed or run on Windows 10 Mobile.

On the development machine, build the bridge from a developer command prompt with a nightly toolchain and its matching `rust-src` installed:

```bat
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x64_arm uwp 10.0.15063.0 -vcvars_ver=14.44
cargo +nightly build --manifest-path uwp\core-bridge\Cargo.toml --target thumbv7a-uwp-windows-msvc -Z build-std=std,panic_abort --release
set "HORIZON_UWP_BRIDGE_LIB=%CD%\uwp\core-bridge\target\thumbv7a-uwp-windows-msvc\release\horizon_uwp_core_bridge.lib"
call uwp\host\build-native.cmd arm
powershell -ExecutionPolicy Bypass -File uwp\host\package.ps1 -Architecture arm
```

The package is `uwp\host\build\arm\HorizonUWP-arm.appx`. Packaging makes an unsigned package; sign it with a code-signing certificate whose subject matches `CN=HorizonUWPPrototype` in the manifest. For a local development certificate, run this in PowerShell. Protect the private key, which remains in the current user's certificate store; only the public certificate is exported.

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=HorizonUWPPrototype' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(3)
Export-Certificate -Cert $cert -FilePath uwp\host\build\arm\HorizonUWPPrototype.cer
& 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe' sign /fd SHA256 /sha1 $cert.Thumbprint uwp\host\build\arm\HorizonUWP-arm.appx
```

The accompanying development certificate must be trusted on the target, and the device must permit sideloading. The manifest also requires a compatible ARM `Microsoft.VCLibs.140.00` framework package. Neither certificate installation nor availability of that dependency on a Windows 10 Mobile 15063 device has been verified. The minimum OS version in the manifest alone does not prove that the binary's APIs or dependencies work on that OS.

For ARM64, use `x64_arm64` in the developer environment, target `aarch64-uwp-windows-msvc`, point `HORIZON_UWP_BRIDGE_LIB` to that target's release library, and run `build-native.cmd arm64` followed by `package.ps1 -Architecture arm64`. The bridge-linked ARM64 shell was registered as a loose developer package and launched on the Surface Pro X (ARM64 OS build 26220); visual output was not verified. An SSH session could not register it directly, so registration and launch were performed in the active interactive session. The temporary scheduled task used for this was removed afterward.

The full Horizon client with its default features passed a release-mode ARM64 UWP `cargo check` with Rust 1.99.1, `RUSTC_BOOTSTRAP=1`, and Cargo's nightly-channel override. This checked Rust metadata; it did not link a game binary, produce a game package, validate AppContainer API use, or exercise the D3D11 shell. Native dependencies in this check were not forced to use `WINAPI_FAMILY_APP`.

```bat
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x64_arm64 uwp 10.0.26100.0 -vcvars_ver=14.44
set "RUSTC_BOOTSTRAP=1"
set "__CARGO_TEST_CHANNEL_OVERRIDE_DO_NOT_USE_THIS=nightly"
cargo +stable check -p horizon-client --target aarch64-uwp-windows-msvc -Z build-std=std,panic_abort --release
```

The internal Cargo override is a temporary diagnostic workaround, not a supported toolchain configuration; use a matching nightly and `rust-src` for repeatable builds.

An attempt to check the full Horizon ARM32 client failed in `ring`: its ARM assembly files were passed to MSVC `cl.exe`, which did not compile them. An experiment with clang and the UWP API family definition progressed to `hidapi`, which uses desktop APIs unavailable under that family. Disabling the client's default audio and controller features bypassed that dependency, but `windows-sys` 0.61.2 then failed to compile because ARM32 definitions including `WSADATA`, `SERVENT`, and `DELAYLOAD_INFO` were missing. The full client has additional UWP and rendering dependencies that have not been ported.

```bat
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x64_arm uwp 10.0.15063.0 -vcvars_ver=14.44
set "CC_thumbv7a_uwp_windows_msvc=C:\Program Files\LLVM\bin\clang.exe"
set "CFLAGS_thumbv7a_uwp_windows_msvc=-DWINAPI_FAMILY=WINAPI_FAMILY_APP"
cargo +nightly check -p horizon-client --no-default-features --target thumbv7a-uwp-windows-msvc -Z build-std=std,panic_abort --release
```
