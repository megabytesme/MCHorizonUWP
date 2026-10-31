# UWP host prototype

This is a native UWP CoreWindow and D3D11 presentation experiment. It currently clears the window to a solid color; it does not launch Horizon. It is intended to establish the app lifecycle and graphics surface needed for the port.

On the development machine, run from the repository root:

```powershell
cmd /c uwp\host\build-native.cmd x64
powershell -ExecutionPolicy Bypass -File uwp\host\package.ps1 -Architecture x64
```

Replace `x64` with `arm64` or `arm` to build the corresponding native host. The scripts use the locally installed VS 18 compiler and Windows SDK 15063 for x64, VS 18 and SDK 26100 for ARM64, and VS 2017 and SDK 15063 for ARM. These paths currently reflect the development machine and may need adjustment on other systems.

Packaging generates an **unsigned** APPX and a loose package directory under `build`. With Developer Mode enabled, the loose x64 or ARM64 package can be registered for local development using `Add-AppxPackage -Register <path-to-package/AppxManifest.xml>`. Installing the APPX itself requires an appropriate trusted signing certificate. The host uses the Microsoft.VCLibs.140.00 framework package; the matching architecture must be installed on the target. ARM32 framework availability and Windows 10 Mobile deployment have not been verified.
