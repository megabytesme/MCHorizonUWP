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

An attempt to check the full Horizon ARM32 client failed in `ring`: its ARM assembly files were passed to MSVC `cl.exe`, which did not compile them. An experiment using clang also needs UWP family settings and further compatibility work. The full client has additional UWP and rendering dependencies that have not been ported.
