# Shared account protocol

`protocol.rs` is the common Microsoft device-code, Xbox/XSTS and Minecraft
services protocol used by the desktop launcher and this UWP-oriented library.
The launcher includes the protocol in its existing account module so its proxy,
keychain and launch behavior stay unchanged. The library uses the WinRT HTTPS
bridge on UWP and deliberately reports a transport error on other targets.

The `vault` module stores one active account in Credential Locker on UWP and
fails closed when storage is unavailable or malformed. The `sign_in` worker
performs the device-code flow, restores an account or signs out without blocking
the UI;
it saves credentials before returning a token-bearing in-memory session. If an
established owner cannot be checked online, it returns an offline-only result.
Workers serialize account changes so a refreshed token is not overwritten by a
second operation. These APIs are not yet connected to the game UI. It does not provide a
sign-in screen or start a game. A UWP host must link both native bridges and
hand the Minecraft session to the game without logging or writing secrets to
ordinary files. A public app registration is still required; no
client ID is included here.

Run the synthetic protocol tests with `cargo test --manifest-path
crates/horizon-account-core/Cargo.toml`. The launcher account tests exercise
the existing desktop protocol and store behavior separately.
