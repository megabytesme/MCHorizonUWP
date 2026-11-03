# Shared account protocol

`protocol.rs` is the common Microsoft device-code, Xbox/XSTS and Minecraft
services protocol used by the desktop launcher and this UWP-oriented library.
The launcher includes the protocol in its existing account module so its proxy,
keychain and launch behavior stay unchanged. The library uses the WinRT HTTPS
bridge on UWP and deliberately reports a transport error on other targets.

The `vault` module stores one active account in Credential Locker on UWP and
fails closed when storage is unavailable or malformed. It does not provide a
sign-in screen or start a game. A UWP host must link both native bridges and
hand the Minecraft session to the game without logging or writing secrets to
ordinary files. A public app registration is still required; no
client ID is included here.

Run the synthetic protocol tests with `cargo test --manifest-path
crates/horizon-account-core/Cargo.toml`. The launcher account tests exercise
the existing desktop protocol and store behavior separately.
