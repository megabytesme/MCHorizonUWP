Builder 1.0.1 follows the Horizon commit pinned by the selected signed patch instead of requiring the original nightly commit. The build summary and client version metadata now keep the selected source revision, and the packaged Horizon release notes identify that exact commit when it does not have a published nightly.

This builder continues to support UWP client 0.1.3 and supports both Horizon patch 0.1.1 and patch 0.1.2 on x86, x64, ARM32 and ARM64. Patch 0.1.2 is based on Horizon upstream main commit e797c725ad99ef311db216de7f41a2e6593353db.

This release retains the prior builder's setup, native-mod compatibility checks, multi-architecture builds and Cargo build-speed control.
