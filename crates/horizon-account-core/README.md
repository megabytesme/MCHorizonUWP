# Shared account protocol

`protocol.rs` is the common Microsoft device-code, Xbox/XSTS and Minecraft
services protocol used by the desktop launcher and this UWP-oriented library.
The launcher includes the protocol in its existing account module so its proxy,
keychain and launch behavior stay unchanged. The library uses the WinRT HTTPS
bridge on UWP and deliberately reports a transport error on other targets.

This library does not provide a sign-in screen, persist credentials or start a
game. A UWP host must link the native HTTPS bridge, keep tokens in Credential
Locker, and hand the Minecraft session to the game without logging or writing
secrets to ordinary files. A public app registration is still required; no
client ID is included here.

Run the synthetic protocol tests with `cargo test --manifest-path
crates/horizon-account-core/Cargo.toml`. The launcher account tests exercise
the existing desktop protocol and store behavior separately.
