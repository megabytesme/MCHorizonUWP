Batch builds can now target x86, x64, ARM32 and ARM64. The finished page groups each architecture’s signed APPX, public certificate, optional resource framework, Microsoft framework dependencies and platform installation guide in its own output folder.

Build speed can be adjusted through the first Rust compile, and compiled Cargo outputs are reused between builds. Minecraft asset progress now settles to Ready after verification. The bridge runtime shim is built with compact Clang for the selected architecture.

The welcome page prepares dependencies only for selected architectures. The signed compatibility catalog is checked before building, and each resulting APPX includes its public certificate.

Extract the ZIP and run Horizon.Builder.App.exe. See README.md and THIRD_PARTY_NOTICES.md for project and dependency information.
