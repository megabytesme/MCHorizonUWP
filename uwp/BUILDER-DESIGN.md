# Horizon UWP Builder: design and validation plan

## Goal

A person starting with a clean, supported Windows PC can install a desktop
builder, choose a Minecraft installation, a Horizon build, target devices and
mods, and obtain installable packages. The builder explains and manages the
required build tools; it must not assume the user has Visual Studio, Rust or a
command-line environment. This is a design, not a claim that clean-machine
provisioning or any distribution channel already works.

The WinUI 3 desktop builder and its downloadable build recipes are versioned
independently. Build recipes contain pinned client and renderer revisions,
target architectures, supported toolchain versions, package identities and
checksums. Changing a client or a mod should not require rebuilding the
builder itself. Downloaded executable build recipes must have a documented
trust and signature policy; never execute unverified content merely because it
appeared in an index.

## User flow

1. Select a target profile (desktop, Surface Pro X, Windows 10 Mobile, or an
   experimentally supported Xbox/CoreOS device) and the Minecraft version.
2. Select an existing Minecraft installation and verify the user's own game
   files. Explain missing files and ownership rather than distributing Mojang
   assets. Resource preparation and output are private to the user.
3. Select a Horizon version, available renderer(s), and compatible mods. The
   app shows which controls are separately installed modules versus mods that
   change game source and therefore require a rebuild.
4. The setup checker identifies missing toolchains, SDKs, target libraries,
   storage and permissions. It explains the publisher, download size, license,
   elevation/restart requirements and next action before invoking installers.
   A blocked or cancelled installer can be resumed without losing work.
5. Build in an isolated workspace, report actionable progress and errors, and
   save a reproducibility manifest. Reuse cached artifacts only when all build
   inputs match; never overwrite a user's existing project or save data.
6. Produce a signed client APPX, its required runtime packages and, when
   verified, the separate base-assets framework. Show local installation and
   device transfer instructions. A desktop signing certificate is not
   automatically trusted by another device; trust and publisher identity must
   be explained and validated.

## Deployment and setup

The builder is a desktop app, not the Windows 10 Mobile client. Investigate a
self-contained Windows App SDK deployment to avoid a separate runtime setup;
measure package size and supported desktop Windows versions. Self-contained
builder deployment does not automatically include the compiler toolchain.

Provide an installer coordinator with a documented, versioned inventory of
Rust, Cargo, the UWP standard-library patches, Visual C++ tools, appropriate
Windows SDKs, packaging tools and architecture-specific runtimes. Prefer
existing compatible installations; otherwise use official installers and
verify downloads. Do not redistribute Microsoft components unless their
terms permit it. In particular, availability and licensing of the older SDK
and toolset used by the 15063 ARM32 build must be proven on a fresh machine.
Prompt only for installers or permissions that actually require user action;
the setup state records progress and survives restart. The build itself uses
all available cores unless the user chooses a limit.

Use official package tooling to create and verify APPX files. Signing keys are
user-controlled; never ship a shared private key, log tokens or upload keys or
game assets. Explain how a generated certificate is trusted on a specific
device, how it affects upgrades and how to remove that trust. A trusted public
distribution signature can later replace the test-signing workflow.

## Resource framework and mods

Proposed design, pending actual desktop and Lumia dependency tests:

- Generate a read-only base-resource framework once per Minecraft version.
  Use a distinct, deterministic package identity per game version, so package
  dependency resolution cannot silently choose incompatible assets. Verify
  the framework's manifest, file inventory and content hashes when reused.
- Game clients declare the matching dependency and resolve its installed
  location through the OS package graph. A framework is versioned and signed
  independently from the client. Never modify its files or write worlds there.
- Keep Horizon-specific resources with the client or a separately versioned
  Horizon dependency. Keep mod assets in the mod or client build as required
  by the mod format. Shared base assets must not silently include mods.
- Save worlds, accounts, settings and user-installed modules in writable app
  storage. An upgrade or failed build must not remove or overwrite these files.
- Deliver touch controls as an independently installed module. The host keeps
  only the small versioned input/action interface and runtime needed to load
  it. Native source/mixin mods may instead need a client rebuild; compiled DLLs
  cannot be assumed loadable from arbitrary writable locations in UWP.

Framework files are not automatically shareable across app identities merely
because they are installed: prove dependency discovery, read access, version
selection, update and removal behavior on Windows 10 Mobile 15063/16241. The
current framework probe uses synthetic data and does not yet prove this on the
Lumia. Microsoft documents package dependency-graph loading and framework
version selection; older device behavior still needs tests.

## Accounts, renderers and devices

The client should offer a pre-game account/renderer screen. Reuse Horizon's
account protocol and ownership checks rather than assuming a Windows sign-in
is a Minecraft session. Store refresh tokens in OS-provided secure storage;
never pass credentials on the command line or write them to build logs. The
15063 ARM32 HTTPS and Credential Locker paths are separate device tests; their
availability does not by itself implement account sign-in.

Make renderer choices reflect real availability: the native FL9_1 renderer
is the current Windows Mobile fallback; ANGLE and future wgpu DX11/DX12 paths
must only be offered after their actual architecture, runtime and device
capabilities are checked. Select before graphics initialization; recover from
a failed choice and retain a known working fallback. Match visual behavior as
closely as possible across renderers and expose meaningful diagnostics.

Audio, network access, controller input and phone vibration need platform
adapters and device tests; a successful build does not establish availability.
The isolated XAudio2, HTTPS, vibration and credential probes track those
dependencies separately.

## Clean-machine acceptance tests

Use a clean Windows Sandbox or VM to install only the builder, then walk through
tool detection, setup, a pinned build and package verification. Capture logs
without credentials or private game assets. Repeat with no existing tools,
interrupted setup, insufficient space, offline operation after caching, a
toolchain update, and cancellation. Sandbox validates desktop provisioning;
it cannot establish ARM32 phone, ARM64 tablet or Xbox runtime behavior.

Then validate framework install/dependency/read access and client upgrades on
the Lumia; install and run ARM64 on the Pro X; verify saves across upgrades;
test renderer fallback, sound, input and authenticated networking on actual
devices. Before release, test the complete documented process from a fresh
Windows install and record which steps still require an administrator, user
interaction, a license or unsupported external software.

## References

- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps
- https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/framework-packages/framework-packages-overview
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-loadpackagedlibrary
- https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-sample-configuration
