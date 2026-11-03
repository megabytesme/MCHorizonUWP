# Codex prompt: Horizon UWP client and AppX builder

## Current integration status — 3 November 2026

The latest work is on the isolated branch
`codex/uwp-nightly-f42ef42` in
`C:/Users/ben/.huwp/b/upstream-fc021-20261103`. Its feature commits are
`8cb8bfeba Add configurable touch controls and device gyro` and
`e3fa11d91 Pin builder to configurable touch client`; the new single-window
commit is `474f38902`. The builder pin and memory-aware Cargo parallelism are
now updated with it. The independent builder MSBuild-path fix is `01dfbcf22`.
No changes were pushed. These current notes supersede older status statements
below when they conflict.

The native touch mod now provides pre-game sliders for touch-look sensitivity
and control size, plus opt-in gyro aiming and its sensitivity. Control size
scales both rendered controls and touch hit areas. Windows sensor access stays
in a UWP adapter; the shared Rust mod contains settings and axis conversion.
Controller vibration remains independent. The pre-game page and game now share
one CoreWindow on both desktop and Windows 10 Mobile. The host replaces the
XAML root with the game surface, restores the page when gameplay exits, and
starts/stops device gyroscope input with the game view's visibility.

After the user's license review, Microsoft.UI.Xaml 2.7.3 was restored to the
per-user NuGet cache and the ARM32 WinUI shell build completed. The clean ARM32
recipe built the native touch, device-vibration, account, audio, HTTPS,
controller and XAML-shell features using seven Cargo jobs on a 32-processor
machine; the builder now caps parallelism to its memory budget. The strict host
link passed and verified its 8 MiB stack reserve. The shell emits existing
WinUI metadata warnings from the legacy ARM SDK, but compiles and links
successfully. The self-contained WinUI 3 builder was republished.
Its executable is
`C:/Users/ben/.huwp/b/upstream-fc021-20261103/uwp/builder/App/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/publish/Horizon.Builder.App.exe`
(SHA-256 `BC27488B4A8234221F0E3786A1239FF3D85CE6562D4E2CDABD7E5251E7025647`).

The signed ARM32 client package is at
`C:/Users/ben/.huwp/b/6dae778f98f74936/deliverables/HorizonUWP-ARM32-signed.appx`
(SHA-256 `7B53C902874452E8EF6359AEA1A8E65F10BF703FB825FE2EA74E752D15F74DE9`).
Its identity is `HorizonUWPLatestPreview`, version `2026.1103.930.55`; the
builder pins client source `474f389024d37f1de80c7e38531b10f991200fde` and embeds
the exact Horizon nightly `2026-11-03` notes. Its same-folder assets framework
is `Horizon-26.3-Assets-signed.appx` (SHA-256
`CA814E4C946A93AE18DC9941A9AB7C197B55FD0350E4B453ECE2E4AFB1921BAD`), and the
Microsoft WinUI dependency is `Microsoft.UI.Xaml.2.7-arm.appx`. Install those
two dependencies before the client on a clean device. The package CMS
signatures, all block-map entries, dependency identity/publisher and ARM
executable were checked. Both Horizon packages use thumbprint
`22A7D038213EF31853EEE277B7C1FAFD576A70AB`; the build account's SignTool reports
the same untrusted self-signed root as for earlier packages. Only the public
certificate is included. Install as an update over the existing same-identity
client; uninstalling first would delete app-local worlds, settings and accounts.

The install bundle, including the public prototype certificate and
`README-ARM32.txt`, is at
`C:/Users/ben/.huwp/b/6dae778f98f74936/deliverables`. The client manifest depends
on both framework packages. The
preserved original asset framework is
`C:/Users/ben/.huwp/b/upstream-fc021-20261103/.local/buildcheck/renderer-dx12-20261103/arm-final/deliverables/Horizon-26.3-Assets-signed.appx`;
its extracted private resource tree is under
`C:/Users/ben/.huwp/b/upstream-fc021-20261103/.local/buildcheck/live-tile/asset-framework-unpacked-gyro/resources`.
Do not commit, delete, overwrite, or otherwise expose either asset copy. The
install bundle is at
`C:/Users/ben/.huwp/b/arm32-touchsens-20261103/deliverables`.
The single-window behavior, touch feel, audio, haptics and game start still need
the user's phone test.

## Published nightly update — 3 November 2026

At the user's request, follow the published Horizon nightly instead of the
moving main branch. The current published source is
`nightly-source/2026-11-03-f42ef42`, commit
`f42ef42ef6ceccb3c6a20bea2481d87949a51c81`. UWP port commit
`1c1a8de62627f05a27a995d7a9319ec9d4673e3c` merges that checkpoint and
passed x64 and ARM32 release builds and strict UWP host links, including the
8 MiB stack check. Device runtime testing is still outstanding. The Minecraft
version and resource preparation code remain 26.3, so the existing asset
framework is reusable. The builder pins the port commit and embeds Horizon's
nightly version fields. It checks GitHub's public nightly release metadata
but does not automatically integrate an unverified upstream revision. Its
default APPX version, `2026.1103.930.50`, encodes the nightly's UTC release
timestamp; use a higher version for subsequent port builds of the same
nightly. Signing remains outside the builder; the local SSH signing flow is
authorized after a client package is produced.

The signed ARM32 nightly client is
`C:/Users/ben/.huwp/b/nightly-f42-arm-20261103/HorizonUWP-nightly-ARM32-signed.appx`
(SHA-256 `4A8431FF31C139630D300744AB174AC7246EA2E1DB599798A18394E4D2EB58DA`).
Its `HorizonUWPLatestPreview` identity is the same as the earlier tested
preview; version `2026.1103.930.50` is newer. It depends on the existing
signed 26.3 framework and Microsoft ARM VC runtime described below. CMS/PKCS7
signature verification, signer thumbprint, every block-map entry, ARM machine
type, executable match and stack reserve were checked. It has not yet been
run on the phone and still uses the interim CoreWindow page.

At the time of this historical note, the real XAML/WinUI pre-game shell was
still in a separate worktree. It has since been integrated into the isolated
branch recorded in the current status above. Preserve the old worktree and its
private test packages unless a later reconciliation explicitly needs them.

## Multiplayer stack fix — 3 November 2026

The user successfully signed in on the separate desktop UWP test, but the app
crashed entering multiplayer. Windows recorded stack overflow 0xc00000fd;
the crash stack points to `Game::new` during the game's UI-thread handoff.
The game host now reserves an 8 MiB stack and verifies the PE header after
strict linking. Both x64 and ARM32 links pass. The user confirmed that the
updated x64 test reaches multiplayer and connects to a server. The test was
intentionally muted and did not test audio.

An updated signed ARM32 package, version 1.0.2.0 of the same
`HorizonUWPLatestPreview` identity, is at
`C:/Users/ben/.huwp/b/stack-fix-arm-20261103a/HorizonUWP-ARM32-signed.appx`
(SHA-256 `6683BDC76E31D8ACFFD78F971C92902D63D6C825E29BC1571A5CE3C550745FB8`).
It retains the previous package's publisher and 26.3 framework dependency.
The package signature, block map, identity and PE stack reserve were verified.
The phone build has not yet been tested. Do not remove or reinstall over user
data; ordinary same-identity upgrading is intended to preserve it.

The builder previously pinned client commit `4145cbcb3d4c338a01401a03c4bf9c5d2cfabf0a`;
the newer pin above includes the same multiplayer stack fix. The user next requested a
pregame design like their WikiViewer app: themed navigation, a home page with
Start game, a profile/account button and a settings page. The three requested
styles follow system accent colors: plain UWP, Fluent Acrylic on supported
Windows 10 versions, and Mica on supported Windows 11 versions. The current
CoreWindow page remains a functional interim UI.

An isolated native C++/CX probe in `.local/xaml-probe` confirmed that a UWP
XAML primary view can create a separate DirectX CoreWindow and both remain
responsive on this Windows 11 desktop. A separate probe loaded WinUI 2.8
navigation controls successfully. The installed WinUI 2.7 framework was in
the probe's package graph, but activation returned 0x80040154 on this PC;
the cause has not been established. WinUI 2.7.3's ARM framework declares a
15063 minimum, whereas the newer framework's requirements must be respected.
Do not change or repair the user's shared WinUI installations to work around
the probe. Phone view switching, native account/session handoff, actual theme
selection and Mica/Acrylic behavior remain to be implemented and tested.
The older Direct2D themed experiment is uncommitted in the separate
`upstream-2e0a6957` worktree and must be preserved.

## Native page and networking update — 3 November 2026

The user reported a crash at the account-service waiting dialog and asked for
a native page and visible diagnostics. The phone was unreachable over SSH, so
the underlying crash has not yet been identified. The UWP host now uses a
CoreWindow/Direct2D startup page instead of a sequence of modal dialogs, with
touch, keyboard and controller input. The ARM32 target aborts on panic; a
panic hook saves a new app-local diagnostic and attempts to show it before
termination. Caught native initialization and run errors are also reported.
Fatal OS failures and forced termination cannot be guaranteed a dialog.

The user also reported the official SMP timing out. Its current SRV record is
`play.playhorizon.dev:24454`; a status request succeeded from the build PC.
The UWP resolver uses WinRT service resolution instead of adapter enumeration.
The native picker mod imports selected .hmod and .zip files without overwriting;
reopen the game screen to refresh. In-game resource-pack reload is still
unsupported by the native renderer; choose packs before entering a world.

The signed ARM32 client is
`C:/Users/ben/.huwp/b/latest-page-arm-20261103a/HorizonUWP-ARM32-signed.appx`,
SHA-256 `D2CC9C3A2BF191B4ACA4B3F5863D8C4D84B3584C3AA1A400CE14B84B13E5C3CE`.
It uses the same signed 26.3 framework listed below and the separate identity
`HorizonUWPLatestPreview`. Its block map and CMS signature were verified.
ARM32 and x64 release bridges and strict UWP links passed; the account and
resolver tests passed. User testing of the page, real account sign-in, network,
pickers and hardware remains outstanding.

At the user's request, a separate x64 developer-mode package named
`HorizonUWPDesktopPage20261103` was registered and opened on the build PC.
Its private asset snapshot and loose package are under
`C:/Users/ben/.huwp/b/desktop-uwp-test-20261103a/`. The marker in that loose
package isolates its game paths and disables audio. Do not delete its app data
or inspect the user's credentials. No certificate trust was changed.

The updated source includes upstream Horizon through
`eb545bab38934988f5ed6ff79064663c240e743d`, in client commit
`9819667e6114aa796ffe51ef522058a804eb8fd4` on the isolated
`codex/uwp-upstream-refresh-2e0a6957` worktree. The builder pins the later stack
fix recorded above; the previously reported builder-signing approval block
still applies.
The native renderer's custom glint and live pack reload and the split-screen
multi-controller route remain incomplete. Historical notes below are
superseded where they conflict with this section.

## Latest account and ARM32 update

The user explicitly authorized the public Fluent Launcher application ID
`0844e754-1d2e-4861-8e2b-18059609badb` as a temporary default while their own
ID is reviewed. Microsoft accepted a device-code request; Xbox/Minecraft sign-in
still needs a real-account/device test. Never read or copy another launcher's
saved credentials. The native UWP pre-game dialogs and in-memory session handoff
are in client source commit `4bcdb5f4e2a05524b5b631738897c0527eecb546`.
The builder pins that source and allows the public ID to be changed or cleared.

The signed ARM32 client and matching neutral resource framework, the Microsoft
ARM VC runtime and public certificate are together under
`C:/Users/ben/.huwp/b/pregame-account/.local/ready-arm/`. The client uses
identity `HorizonUWPAccountPreview`, distinct from the earlier installed test.
Both signed packages' CMS signatures were checked; the self-signed certificate
is not trusted on the build PC and device trust may need setup. No trust store was
changed. They contain the user's private resources; never commit or upload them.
The user has not yet tested the new account, controller, vibration, or framework
integration on a device.

The upstream refresh in `C:/Users/ben/.huwp/b/upstream-2e0a6957` has had its
textual merge conflicts resolved but still has UWP compilation work to complete.
It must not replace the working integration branch until it passes tests and
includes the new account and input features. Later historical notes below may
say the pre-game UI is absent or list older source pins; this section supersedes
those statements.

## Integration update — 3 November 2026

The active continuation is the isolated worktree `.local/client-builder-integration`,
branch `uwp-client-builder-integration`. The main `uwp-dx11-port` worktree's
pre-existing changes were left in place. The instructions below are historical
where they conflict with this update; their safeguards and remaining scope still apply.

- Native touch, XAudio2 and WinRT HTTPS are combined. Both x64 and ARM32 game
  bridges and hosts passed the SDK 15063 strict link; the combined builds have
  not been installed or exercised on the target devices. The ARM32 offline
  dependency override still leaves non-adapted HTTPS paths unavailable.
- Unsigned, private test packages include 4,961 verified sound files and
  `sounds.json`. They use distinct identities so they do not replace the
  phone's existing game. The original unsigned packages are:
  - x64: `.local/client-builder-integration/.local/combined-x64-unsigned/HorizonUWP-x64-with-sound.appx`,
    identity `HorizonUWPIntegrationX64`, SHA-256
    `e40f2f69787122994ae506e54892e22e2e99ef7eb2d6442623dca9d0420a5b4f`.
  - ARM32: `C:/Users/ben/.huwp/b/0e7ac4615b164865/combined-arm-unsigned/HorizonUWP-arm-with-sound.appx`,
    identity `HorizonUWPIntegrationArm`, SHA-256
    `9026deeea1ef5a6958708cec2d26f30c67519861951a1b8a788ba449c55f722c`.
  Both use publisher `CN=HorizonUWPPrototype`. The previous no-sound packages
  were retained. Do not upload, commit or redistribute any of these packages.
- Signed copies were created through the user's local SSH account without
  exporting the private key or changing certificate trust:
  - x64: `.local/client-builder-integration/.local/combined-x64-unsigned/HorizonUWP-x64-with-sound-signed.appx`,
    SHA-256 `125b7b5be518818dab44b1ed3bdb6a6f349041ef7ce65fdbb6d73ebbc8e8b66a`.
  - ARM32: `C:/Users/ben/.huwp/b/0e7ac4615b164865/combined-arm-unsigned/HorizonUWP-arm-with-sound-signed.appx`,
    SHA-256 `8beb685bde49e8eec8810e43b4a0c5504f8545a82ea99b3d9010f77d48233a81`.
  ZIP integrity, signature entries and CMS signature cryptography were checked.
  Windows SignTool reports the self-signed root is untrusted on this PC;
  installation and target-device trust remain to be checked by the user. The
  signed copies contain private assets and predate the session-handoff work below.
- The stale `localhost` SSH host-key record was replaced with the already
  trusted Ben-Desktop key from `192.168.1.121`. Normal verified localhost SSH
  works; the original known-hosts file is backed up as
  `C:/Users/ben/.ssh/known_hosts.before-codex-7339caa402cc402fac1874681114e178`.
- The builder's pinned client source is `2b0df3b55786186db71008e35de619a0f85b2342`.
  It fetches target and x64-host Cargo dependencies before an offline build,
  and can prepare missing sounds in a private snapshot from a selected Minecraft
  installation. Fresh-cache offline checks passed for x64 and ARM32; the sound
  preparation step was exercised on the user's local installation without
  changing the selected resources. The full x64 and ARM32 recipes ran
  successfully on this PC through the Rust build, strict link, framework and
  client packaging. Their unsigned results are under
  `C:/Users/ben/.huwp/b/ff4fcd97a73644bc/` (x64) and
  `C:/Users/ben/.huwp/b/7e177e46f2d0484c/` (ARM32). Both frameworks have the
  same content-based identity. These clients use the default
  `HorizonUWPPrototype` identity and were not installed; they could replace
  an existing package of that identity. The recipe has not yet been run end to
  end from a clean Windows installation.
- A self-contained builder publish is at
  `.local/client-builder-integration/uwp/builder/App/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/publish/`.
  Keep the publish folder together. It is a development build, not an installer.
- The shared account protocol, Credential Locker storage and cancellable
  sign-in/restore worker are committed. Synthetic tests pass and the account
  crate type-checks for x64 UWP. The client has an in-memory hosted session entry
  point that rejects a second active account and ignores inherited session files;
  focused tests pass. The pre-game UI and native host wiring to the account
  worker are **not implemented**, and no real sign-in has been tested.
- The builder can package a prepared resource tree as a separate unsigned,
  architecture-neutral framework and create a client with a matching package
  dependency. The identity includes the Minecraft version and a digest of the
  staged files, so distinct resource trees get distinct identities. The packer
  accepts other numeric versions, while the client recipe remains pinned to
  26.3. x64 and ARM32 hosts with framework lookup passed strict UWP links;
  synthetic packaging and dependency checks passed. Runtime framework lookup
  has **not** been tested on a device.
- A private 26.3 framework and dependent clients were packaged and verified
  without installation or signing. They contain the user's resources and
  must not be committed, uploaded or redistributed. The two client executables
  were linked from the previously built bridges and predate the session-handoff
  work; their identities differ from the earlier signed test packages:
  - Framework: `C:/Users/ben/.huwp/b/resource-framework-0c470957614241f7817078a365b95cb2/Horizon.Asset.26-3.321aa1f73c8a1a627df3642f1dcee0.appx`,
    SHA-256 `25a342b858ccc082df2483373f85897823195efd455015fc90448a833d380974`.
  - x64 client: `C:/Users/ben/.huwp/b/resource-framework-0c470957614241f7817078a365b95cb2/client-x64/HorizonUWP-x64.appx`,
    SHA-256 `8403ee5fe19db4bb258c1447743c81dac422cbb8aa581c0047605cefde46c411`.
  - ARM32 client: `C:/Users/ben/.huwp/b/resource-framework-0c470957614241f7817078a365b95cb2/client-arm/HorizonUWP-arm.appx`,
    SHA-256 `893daed6209539c6b05fc8a4a5312ca56b087b1ca9fb05cd45d9a1ae5deb7ad8`.
  All three passed ZIP and manifest checks; the framework contains 4,961 Ogg
  sounds and matching sound metadata. They require signing and publisher trust.
- A public-only export of the prototype signing certificate is at
  `C:/Users/ben/.huwp/b/resource-framework-0c470957614241f7817078a365b95cb2/HorizonUWPPrototype-public.cer`.
  Its thumbprint is `22A7D038213EF31853EEE277B7C1FAFD576A70AB`; it was checked
  to contain no private key. No certificate trust store was changed.
- Renderer selection beyond the native D3D11 recipe, ARM64, clean-machine
  setup, builder signing, Sandbox validation and device checks remain open.
- An automatic approval review blocked the command to create the
  certificate-store signing implementation, with the stated reason "blocked by
  policy". Do not bypass that review. The user separately authorized signing
  the existing test packages over SSH; those signed copies are listed above.
  No certificate, private key or trust store was changed. Resolve the review
  before implementing the rejected builder signing workflow.
- The client build exhausted memory with all 32 jobs, even after running alone;
  both successful combined builds used 8 Cargo jobs. Increase concurrency only
  if available memory permits. No commits contain co-author trailers; do not push.


Paste the prompt below into the new Codex instance.

---

## Integration update (2026-11-03)

The user explicitly authorized using Fluent Launcher’s published Microsoft client ID
as a temporary default while their own application is reviewed, superseding the
older instruction below not to use another application's ID for this case. The
public ID is `0844e754-1d2e-4861-8e2b-18059609badb`; Microsoft accepted a
device-code request without any saved-token access. Minecraft/Xbox login using
that ID still requires real-account validation. Do not expose, copy, or overwrite
existing launcher credentials.

This update supersedes older branch-status statements below. The active integration
worktree is `.local/client-builder-integration` on `uwp-client-builder-integration`.
It combines the phone-tested touch, XAudio and WinRT HTTPS adapters, shared account
protocol and Credential Locker core, and asset-framework builder work. Microsoft
sign-in and the native UWP pre-game dialogs are connected in the latest source;
real-account and device testing remain outstanding. The combined signed
ARM32 and x64 sound packages are being tested by the user; do not overwrite them.

The integration branch removes Wasm parsing from the UWP dependency graph. The
Windows.Gaming.Input bridge and optional `horizon_device_vibration` native mod
are in commit `03c0832c5354b3522b24f564bf843ffc37368953`; their combined
x64 and ARM32 release bridges and strict UWP host links succeeded. Its persistent in-game setting is independent of controller
vibration. The phone vibration API was previously tested as a probe; the combined
mod and generic tablet haptics still need physical-device testing.

A separate uncommitted merge of upstream `origin/main` at
`ccf8f453fb55e146f48964d4cd11d350cb586d53` is in
`C:\Users\ben\.huwp\b\upstream-2e0a6957` on branch
`codex/uwp-upstream-refresh-2e0a6957`. Its textual conflicts are resolved, but the port is incomplete and unverified; preserve that worktree and do not replace the stable integration branch
with it until the update is reconciled and tested. Upstream split the client
library and made major renderer, account and game changes. Keep UWP functionality
in native mods or small platform adapters where possible, without losing upstream
features.

You are continuing the Horizon UWP client and desktop AppX builder project. The user wants a polished WinUI 3 desktop app that can prepare and build a Horizon UWP client and its optional asset-framework package, with a pre-game client experience for Microsoft/Minecraft account sign-in, renderer choice, and later platform features.

## Scope

Work only on the Horizon UWP client and the WinUI 3 AppX builder. A separate Codex agent owns the wgpu Direct3D 11 fork at `C:\dev\personal\wgpu`; do not inspect, modify, build, or commit to that repository or its worktrees.

The Horizon source repository is `C:\dev\personal\Codex\MCHorizonUWP`. The main worktree is on `uwp-dx11-port`; inspect its current HEAD and status before working. It has pre-existing unrelated modified and untracked files; preserve them. Use isolated worktrees/branches for integration. Do not reset, clean, or overwrite user data.

Follow the user's `AGENTS.md`: use all available CPU cores for builds, install dependencies only through safe documented package-manager flows, commit complete features in the user's style, and never add a `Co-authored-by` trailer. Do not push. Avoid GUI/computer use unless there is no CLI alternative. Report any required account registration, license, administrator action, or device interaction precisely. Do not commit, upload, or expose the user's Minecraft assets, saves, tokens, or private keys.

## Current branches and work

- Device-tested client base: `C:\dev\personal\Codex\MCHorizonUWP\.local\phone-d3d11`, branch `uwp-phone-d3d11`, commit `43c80ed`.
- Native touch module: `.local\native-touch-mod`, branch `uwp-native-touch-mod`, commit `11df776`. Commit `5a103f7` removes the old Wasm importer/runtime. The native source mod is `mods/horizon-touch-native`, selected by build feature and stable ID `horizon_touch`; it appears in the existing Mods UI and its toggle enables/disables controls. Local ARM32 and strict SDK 15063 checks passed. The user confirmed native touch works, including inventory Back. A signed 1.0.9.0 ARM32 settings-test package is at `C:\Users\ben\Documents\Codex\2026-10-31\how-feasible-is-a-uwp-port-2\outputs\Horizon-Native-D3D11-ARM32-Touch-Mod-Settings.appx`; its new Mods-list toggle has not yet been checked on-device. Notes: `C:\Users\ben\Documents\Codex\2026-10-31\how-feasible-is-a-uwp-port-2\outputs\Horizon-Touch-Mod-Settings-README.txt`. An obsolete Wasm importer experiment is safely stashed as `c1c9f709450cc9efbbe4f99701b679e4df219172` on `.local\touch-import-fix`; do not reapply it.
- Audio: `.local\audio-xaudio`, branch `uwp-audio-xaudio`, commit `1bff0da`. The user confirmed XAudio2 tones and in-game audio work on ARM32. This is not integrated with the current native-touch branch; the merge overlaps workspace/bridge manifests.
- HTTPS and account probes: `.local\https-probe`, branch `uwp-https-probe`, commit `b8b07e2`. User-tested UWP HTTPS returned Mojang HTTP 200 and rejected an expired certificate; the user also confirmed the credential probe. A native WinRT HTTP bridge and PasswordVault wrapper exist, but neither is wired into the complete client sign-in flow. `.local\account-core` is clean on `uwp-account-core` at `b8b07e2`; the unfinished shared account-protocol extraction is preserved in stash `336eb50afac3683eba54055640ed6fb58540f90d` (message: `WIP shared account protocol extraction for UWP`). Two synthetic core tests passed; launcher regression tests could not run offline because the `dirs` crate was unavailable. Treat extraction as incomplete and inspect the stash before continuing.
- Builder: `.local\builder`, branch `uwp-builder`, commit `24a4de9`, clean. It includes a self-contained WinUI 3 publish, prerequisite checks, interactive fixed-ID WinGet setup for Rustup/Git/Python, deterministic build inputs, a local isolated recipe runner, native-touch selection, package-version selection, and a synthetic framework packer. It built both target packages described below. The clean Sandbox lacked WinGet; Visual Studio/old SDK installation and complete fresh-machine provisioning remain unfinished. A direct Rustup bootstrap download/execution attempt was rejected by automatic policy; do not bypass that policy.
- Renderer selection work exists at `.local\render-selection`, branch `uwp-render-selection`, commit `942b4ac`; inspect it before duplicating the feature.

## Verified package and runtime facts

- The builder completed strict x64 and ARM32 UWP builds from the cleaned native-touch source, using the user's local prepared Minecraft 26.3 resources. Both packages are unsigned and have not been installed:
  - `C:\Users\ben\.huwp\b\19d3bcec7df249ba\package\HorizonUWP-x64.appx` — 1.0.10.0, x64, 66,816,774 bytes, SHA-256 `5994419C189DD3A89ED96AD034426329A3D454C2A462DAFB9FF0630301B4CB73`.
  - `C:\Users\ben\.huwp\b\64f57f68ef3044c1\package-v1.0.10.0\HorizonUWP-arm.appx` — 1.0.10.0, ARM32, 63,952,752 bytes, SHA-256 `05754A17DA8C385358778BC86657D7DC2A39832248B06EF111002C1C1E109CCE`.
- These runner packages use identity/publisher `HorizonUWPPrototype` / `CN=HorizonUWPPrototype`. The Lumia's existing game package uses a different identity (`HorizonUWPNativePhone`). The builder has not implemented signing. Any signing workflow must verify publisher identity and keep private keys user-controlled; do not pass PFX passwords on the command line, export keys, persist secrets, or change trust stores automatically.
- Resource framework model: a synthetic x64 UWP framework was installed in a clean Windows Sandbox. Two separately installed client packages each enumerated `Package::Current->Dependencies` and read a marker from the framework's `InstalledLocation`; both passed. This proves desktop package sharing, not Minecraft asset packaging. Synthetic ARM32 framework/client packages were built with SDK 15063 but have not been run on the Lumia. Test package files and notes are in `C:\Users\ben\Documents\Codex\2026-10-31\how-feasible-is-a-uwp-port-2\outputs\AssetFrameworkProbe-ARM32`.
- Intended asset split: immutable base-game assets go into a separately versioned framework per Minecraft version; Horizon-specific and mod assets stay with the client/module. Worlds, settings, credentials, and other writable state stay in app data. Keep framework manifests and content hashes deterministic.
- The phone currently runs at about 15–30 FPS on minimum settings. The user confirmed sound, HTTPS certificate validation, Credential Locker, phone vibration, native touch input, and inventory Back in separate ARM32 builds/probes. These features still need integration and testing together.

## Account and platform constraints

Reuse Horizon's existing Microsoft/Xbox/Minecraft authentication protocol in `launcher/src/account.rs`; do not create a parallel protocol that drifts. Continue the preserved account-core extraction only after reviewing its stash, then connect the shared protocol to the WinRT HTTPS and PasswordVault adapters, pre-game sign-in UI, and safe session handoff. Do not log tokens or prompt the user to reveal passwords/secrets in chat.

No `HORIZON_MSA_CLIENT_ID` is configured in this environment. The existing launcher documentation calls for a public Microsoft Entra app registration with device-code flow; Minecraft services may also require Mojang app-ID approval. Implement the flow/configuration without inventing or borrowing another application's client ID. Ask the user for their public client ID or registration action only when code is ready to use it.

UWP LAN networking is already enabled in the main branch. Keep account HTTPS separate from game-server networking. Add platform audio, controller/vibration, and renderer choices through explicit adapters. Renderer choices must reflect actual device support and retain the working native D3D11 path; do not show unavailable renderers.

## Immediate work order

1. Inspect the current worktree/branch state and read `uwp/BUILDER-DESIGN.md` plus `.local\builder\uwp\builder\README.md`. Preserve all pre-existing dirty files. Build an isolated integration branch before combining client features.
2. Bring the phone-tested client, native touch module, XAudio2 output, and secure WinRT HTTP/Credential Locker path together. Resolve the manifest conflicts on the isolated branch. Keep the touch controls as a source/build-time mod, not a Wasm importer and not hard-wired into the base app.
3. Continue the shared account protocol and pre-game UI/session handoff. Identify exactly which app-registration action or public client ID remains user-dependent.
4. Turn the synthetic framework proof into real per-version base-resource framework packaging, with matching client dependency manifests and package-graph lookup. Test x64 in Sandbox, then ask the user to run ARM32 packages on the Lumia and ARM64 packages on the Pro X when ready.
5. Finish the builder's checked build plan, user-controlled signing, certificate/publisher validation, and clean-machine setup guidance. Preserve unsigned outputs until the user chooses a signing identity. Use the existing x64/ARM32 artifacts as inputs when possible instead of repeating full builds.
6. Test the combined app in the clean Sandbox and on the actual target devices. Do not uninstall/replace any existing same-identity package without protecting its LocalState/worlds. Keep private assets outside Git.

Make small feature-complete commits, no co-author trailers. Stop and report blockers that require the user's credentials, Microsoft app registration, licenses, administrator approval, or device interaction. At the end, give exact branch/commit and installable artifact paths, and list tests run and remaining device checks.

---
