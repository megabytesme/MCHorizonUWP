# Native UWP pre-game account flow

`uwp-account` adds a native CoreWindow dialog flow before the game starts. It
supports offline play, Microsoft device-code sign-in, restoring the saved account
and explicit sign-out. Dialogs use at most two actions so they work on Windows
10 Mobile. The player approves sign-in in a browser on another device; no access
or refresh token is shown, logged, sent to the builder, or put in an argument.
The in-memory game session comes from the shared Horizon account protocol, and
the refresh credential is held in the app's Credential Locker. Sign-in warns
before replacing the one saved account; sign-out asks before removing it.

The builder passes the public `HORIZON_MSA_CLIENT_ID` into the Rust build. It
defaults to Fluent Launcher's publicly documented client ID at the user's request;
a different approved ID can be supplied or the field cleared. Microsoft accepted
a device-code request for the temporary ID, but a real Xbox/Minecraft sign-in
and the UI on target hardware remain to be tested.

Host builds with this feature also require `HORIZON_UWP_ACCOUNT=1` and
`HORIZON_UWP_HTTP=1`. The first links the dialog and Credential Locker adapters;
the second supplies HTTPS. The strict link rejects unsupported desktop imports.
