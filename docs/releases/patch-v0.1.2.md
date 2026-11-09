Horizon patch 0.1.2 rebases the MCHorizonUWP changes onto upstream Horizon commit e797c725ad99ef311db216de7f41a2e6593353db. This is the current upstream main commit, rather than a published nightly tag.

The rebase updates the UWP client and renderer integration for upstream changes and wires the shared Horizon client library to the UWP HTTPS bridge used by telemetry. The patch was checked against a clean checkout of its pinned commit and the game bridge passed Cargo checks for x86, x64, ARM32 and ARM64 with the project's UWP feature set.

Build this patch with UWP client 0.1.3 and MCHorizonUWP Builder 1.0.1 or later when allowed by the signed compatibility catalog.
