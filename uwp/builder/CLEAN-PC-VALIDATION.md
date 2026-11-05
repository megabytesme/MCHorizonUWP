# Fresh-PC builder validation (2026-11-05)

This is a failed end-to-end acceptance test, not a release qualification.
The builder was published from commit `e3372d9bd` plus the startup and target
selection fixes in this branch. Its client recipe pins `8b7ab39874de3dbc3a32ce683f86e0e5dd6cc46d`.

## Environment and result

On Ben-Desktop, publish with `uwp/builder/build.ps1 -Publish` and copy only
the resulting self-contained x64 publish directory into a new Windows Sandbox.
Start the Sandbox with `wsb start --config
'<Configuration><MemoryInMB>8192</MemoryInMB><Networking>Enable</Networking></Configuration>'`.
An additional writable temporary share held only the test script and results;
no source tree, build cache, Minecraft files, certificates, or account were
provided to the guest. Windows reported 8,588,824,576 bytes of memory. The
guest had no Git, WinGet, Rustup, Cargo, Python, or .NET command on PATH.

The original publish crashed during XAML loading: its application PRI was
missing. Copying the PRI exposed a second startup problem, addressed by
subscribing to selection changes after the XAML tree is initialized and adding
the WinUI control resources. The corrected publish opened and responded.
UI Automation selected all four target choices. The default was x64 on this
x64 guest. Clicking **Build unsigned client** without other inputs returned
"Choose a local source tree." Clicking **Install Rustup** returned "Windows
Package Manager was not found." The setup scan reported missing tools, SDK
and C++ UWP tools. No game or resource APPX was built in this Sandbox.

## Acceptance gaps

- The user must supply a Git checkout containing the pinned port revision;
  the published builder does not acquire it. The published builder itself is
  currently x64-only, so native x86 and ARM64 builder distribution is not
  established.
- Minecraft 26.3 prepared resources must already exist, or a compatible
  framework must already be installed on the target. The builder does not sign
  in, check ownership and prepare all assets from a new user's account.
  Embedded, newly packaged framework, and existing framework are choices,
  but the full fresh-PC flows have not been validated.
- Two known source mods are checkboxes; browsing and selecting compatible
  native mods from a catalog is not implemented.
- Packages are unsigned; certificate creation, export, trust, local install
  and platform-specific installation guidance are not integrated.
- Git, Python, Rust, matching rust-src, Visual C++ UWP tools, Clang for ARM,
  SDKs and packaging tools are not provisioned end to end. The existing
  buttons depend on WinGet, absent in this Sandbox. Build scripts also depend
  on particular Visual Studio paths and older SDK/toolset versions. Each
  architecture still needs a fresh-machine build and package verification.
- Sandbox is a desktop host test, not proof of package installation, account
  sign-in or game operation on a Lumia, ARM64 device or other target.

## Toolchain approach to investigate

Use a pinned, resumable setup coordinator and official, verified installers
with specific required components, detecting existing compatible installations.
Microsoft documents command-line component selection for Visual Studio Build
Tools and lists v141 ARM and UWP components; the exact older 15063 SDK,
toolchain combination, unattended behavior, space, licensing, elevation and
restart requirements still need a clean-machine trial. A compact download
cache may be possible, but do not assume compiler/SDK files can be extracted
and redistributed: check each component's redistribution terms first. The
user may still need to approve elevation or license terms when required.

References: [Build Tools component IDs](https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-build-tools),
[Visual Studio installer command line](https://learn.microsoft.com/en-us/visualstudio/install/use-command-line-parameters-to-install-visual-studio),
[Visual Studio redistribution](https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution),
and [Windows SDK downloads](https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads).
