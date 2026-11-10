MCHorizonUWP client 0.1.4 updates the client source and bridge inputs for Horizon patch 0.1.2, based on upstream commit `e797c725ad99ef311db216de7f41a2e6593353db`.

The archive includes the client source and prepared host link inputs for x86, x64, ARM32 and ARM64. It also carries the updated Horizon heartbeat feature wiring, preserves depth-tested glint metadata for the UWP renderer, and prepares ARM32's xxhash dependency to use its portable implementation.

This archive is a builder input, not an installable APPX. Build it with MCHorizonUWP Builder 1.0.2 or a later version allowed by the signed compatibility catalog. The Builder creates and signs the target-specific APPX and exports its public certificate.
