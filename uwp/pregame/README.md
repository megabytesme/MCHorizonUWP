# Native UWP pre-game account flow

`xaml-shell` adds the native UWP pre-game page and account flow before the game
starts. It supports offline play, Microsoft device-code sign-in, multiple saved
accounts and explicit sign-out. Dialogs use at most two actions so they work on
Windows 10 Mobile. The player approves sign-in in a browser on another device;
no access or refresh token is shown, logged, sent to the builder, or put in an
argument. The in-memory game session comes from the shared Horizon account
protocol, and refresh credentials are held in the app's Credential Locker.

The builder passes the public `HORIZON_MSA_CLIENT_ID` into the Rust build. It
defaults to Fluent Launcher's publicly documented client ID at the user's request;
a different approved ID can be supplied or the field cleared. Microsoft accepted
a device-code request for the temporary ID, but a real Xbox/Minecraft sign-in
and the UI on target hardware remain to be tested.

The builder embeds the configured public Microsoft client ID, the exact Horizon
nightly and upstream source commit, and the UWP port commit. The About page shows
those build details, architecture, Windows device family and package version;
What's New uses notes fetched for the exact pinned release.

The Start page profile image opens account management. Settings can opt into a
live tile showing recent worlds, the selected account and its downloaded
Minecraft face, or a rotating mix. The tile is disabled until selected. Pin the
app to Start for Windows to display its tile updates. Face images are reduced to
an 8-by-8 local tile image; access and refresh tokens are never included.

Windows 10 Mobile switches between the pre-game and game views instead of
requesting a separate standalone window. On game exit it returns to the pre-game
page. UWP narrator speech is unavailable in the app container; the game reports
it as inactive without logging the expected missing SAPI class.

Host builds with this feature also require `HORIZON_UWP_ACCOUNT=1` and
`HORIZON_UWP_HTTP=1`. The first links the account and Credential Locker adapters;
the second supplies HTTPS. The strict link rejects unsupported desktop imports.
