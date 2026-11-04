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

The Start page profile image opens account management. Settings control the
primary live tile's rotating account, recent-world and server updates. World
updates include the save icon and summary when available; server updates show
the status, player count and icon. Status is refreshed while the launcher is
running, rather than by a background service. Servers and worlds can also be
pinned separately from their pages; opening one of those tiles starts that
saved server or world. Windows controls whether live tiles are displayed. Face
images are reduced to an 8-by-8 local tile image; access and refresh tokens are
never included.

Settings can start Horizon's `--benchmark standard` game benchmark. It keeps its
JSON reports in `game/benchmarks`, accessible from the reports button. The
benchmark creates its own marked world and replaces only that world on later
runs. The optional contribution checkbox applies to the current run: it sends a
privacy-filtered report and hardware details to Horizon, without changing any
other telemetry choices. If uploading fails, the local report is still saved.

The pre-game page and game use the same app view and CoreWindow on every device.
The host clears the XAML root while the game renders into that window, then
restores the pre-game page when the game exits. This also keeps Windows 10
Mobile within its single-window model. UWP narrator speech is unavailable in the
app container; the game reports it as inactive without logging the expected
missing SAPI class.

Host builds with this feature also require `HORIZON_UWP_ACCOUNT=1` and
`HORIZON_UWP_HTTP=1`. The first links the account and Credential Locker adapters;
the second supplies HTTPS. The strict link rejects unsupported desktop imports.
