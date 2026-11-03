# Codex prompt: Horizon UWP client and AppX builder

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
  ZIP integrity and signature entries were checked. Windows SignTool reports
  the self-signed root is untrusted on this PC; installation and target-device
  trust remain to be checked by the user. The signed copies contain private assets.
- The stale `localhost` SSH host-key record was replaced with the already
  trusted Ben-Desktop key from `192.168.1.121`. Normal verified localhost SSH
  works; the original known-hosts file is backed up as
  `C:/Users/ben/.ssh/known_hosts.before-codex-7339caa402cc402fac1874681114e178`.
- The builder's pinned client source is `2ce3d73dbc9e82634b73d65606b3cb14a4e63347`.
  It fetches target and x64-host Cargo dependencies before an offline build,
  and can prepare missing sounds in a private snapshot from a selected Minecraft
  installation. Fresh-cache offline checks passed for x64 and ARM32; the sound
  preparation step was exercised on the user's local installation without
  changing the selected resources. The full updated builder recipe has not yet
  been run end to end from a clean Windows installation.
- A self-contained builder publish is at
  `.local/client-builder-integration/uwp/builder/App/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/publish/`.
  Keep the publish folder together. It is a development build, not an installer.
- The shared account protocol, Credential Locker storage and cancellable
  sign-in/restore worker are committed. Synthetic tests pass and the account
  crate type-checks for x64 UWP; the pre-game UI, native host wiring and session
  handoff are **not implemented**, and no real sign-in has been tested.
- Real versioned asset-framework packaging and package-graph lookup are not
  implemented. The synthetic framework proof remains the only framework test.
  Renderer selection beyond the native D3D11 recipe, ARM64, clean-machine setup,
  signing, Sandbox validation and device checks also remain open.
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
