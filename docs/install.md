# Installing a Horizon UWP package

This guide describes the signed output. Installation is not yet automatic, and
these steps have not yet been verified on a Windows 10 Mobile device.

Keep the client APPX, its public `.cer` file, and any separately built resource
framework together. The resource framework contains your Minecraft resources;
keep it private. The public certificate does not contain your private signing
key. The client also depends on Microsoft.UI.Xaml 2.7 and Microsoft.VCLibs
140.00; their Microsoft-signed packages are included in the `Dependencies`
folder. The builder restores the pinned WinUI package when needed; the Visual C++
runtime package must currently be available in the installed UWP SDK.

## Windows 10 or Windows 11 PC

Choose the package matching the target Windows architecture: x64 for most
Intel and AMD PCs, x86 for 32-bit Windows, and ARM64 for Snapdragon or Microsoft
SQ PCs. On the target, enable sideloading if its Windows version requires it.

For a locally generated signing certificate, import the `.cer` into **Local
Computer > Trusted People** using the certificate import wizard. This requires
administrator approval. Trust a certificate only if you recognize its owner:
Windows will trust other packages signed with the same certificate too. See
[Microsoft's certificate instructions](https://learn.microsoft.com/en-us/windows/msix/app-installer/troubleshoot-appinstaller-issues#trusted-certificates).

Install the Microsoft.VCLibs 140.00 and Microsoft.UI.Xaml 2.7 packages from
`Dependencies` and, if present, the resource framework first;
then open the client APPX in App Installer. Alternatively, use PowerShell's
`Add-AppxPackage` with the matching framework packages. A resource framework
already installed on the target must have the exact name and publisher expected
by the client. An embedded-resource client does not need that framework.

## Windows 10 Mobile

Select ARM32 in the builder. Enable Developer Mode, Device discovery and Device
Portal on the phone, then pair from a PC using the address and pairing code
shown by the phone. Microsoft's [phone setup guide](https://learn.microsoft.com/en-us/previous-versions/windows/uwp/debug-test-perf/device-portal-mobile)
describes USB and local-network connections.

In Device Portal's app manager, select the client APPX and its `.cer`, then add
the matching Microsoft.VCLibs and Microsoft.UI.Xaml frameworks and, when used, the resource framework
as dependencies. Start the installation and check its result before closing the
portal. The exact portal controls can vary by Windows version; see Microsoft's
[app deployment guide](https://learn.microsoft.com/en-us/windows/advanced-settings/device-portal#install-sideload-an-app).

The package's signing identity must match the certificate, and a separately
built resource framework must use the same publisher as the client. Keep the
signing key on the original build PC if you want later updates to retain the
same app identity.
