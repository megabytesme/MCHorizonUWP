# Experimental UWP stdio patch

`stdio-uwp.patch` is an isolated proof of concept for Rust 1.99.1's `rust-src`. It selects the standard library's existing unsupported stdio backend on UWP. As a result, stdin reaches EOF and stdout/stderr report successful writes while discarding their contents. UWP logging needs a separate implementation. It does not make all of Rust std or Horizon UWP compatible.

`file-open-uwp.patch` routes standard file and directory opens through
`CreateFile2`, and avoids desktop-only module lookup when formatting NTSTATUS
errors. An x64 UWP probe successfully created a directory, wrote, read,
appended, tested exclusive creation, truncated, and removed a file and
directory in LocalState. Other filesystem operations and ARM64 have not yet
been validated.

`module-uwp.patch` uses the conservative unloadable-module path for TLS and
the existing fallbacks for optional Windows functions. `runtime-uwp.patch`
adds a process-local environment map (with ASCII case folding), returns
`Unsupported` for subprocess creation and UWP junction/reparse operations,
uses `CopyFile2` and `UnlockFileEx`, and exposes null console handles. An x64
strict UWP probe verified environment get/set/enumeration/removal, unsupported
process spawning, null stdout handle, directory enumeration, rename, copying,
file locking and unlocking, and four-thread `parking_lot` contention.
These are UWP-specific semantics and do not provide desktop child processes,
inherited OS environment variables, junctions or reparse-point inspection.

Apply the patch only to a **copy** of the matching `rust-src` tree, not to the installed toolchain. `prepare-source.ps1 -Destination <new-directory>` creates a copy and applies the patch without modifying the toolchain. With Cargo's internal, unsupported `__CARGO_TESTS_ONLY_SRC_ROOT` environment variable set to the absolute path of the copied `library` directory, `RUSTC_BOOTSTRAP=1 cargo +stable build -Zbuild-std=std,panic_abort --target x86_64-uwp-windows-msvc --release` can build against that copy. This Cargo override is a test-only interface and may change. Use a desktop MSVC developer environment for Cargo's build scripts and a UWP environment for the final native host link.

An isolated static library whose only export writes to stdout built against the patched std. It linked with `/NODEFAULTLIB:kernel32.lib /OPT:REF WindowsApp.lib ntdll.lib`; the resulting x64 PE did not import `GetStdHandle`, `CreateFileW`, `CreateProcessW` or `ntdll.dll`. The link required `ntdll.lib` to resolve references in pulled-in std object files even though `/OPT:REF` removed those imports from this small executable. Inspect every final executable's imports; this experiment does not show that larger binaries avoid unsupported calls.

Other standard-library paths and dependencies may still use unsupported APIs.
The strict link must reject any such live references; changing import libraries
alone does not make those APIs UWP compatible.

## Strict linking of the x64 wgpu probe

Rust's static library contains large object files with references to desktop
APIs, even when the corresponding functions are dead. The native linker reports
these references before `/OPT:REF` removes them. `strict-link.ps1` resolves the
known references with link-only sentinels, then checks the generated map and
deletes the executable if any sentinel survived. It must be run from a UWP
MSVC developer environment, with compiled UWP host object files and a Rust
static library already built. For example:

```powershell
./uwp/rust-std/strict-link.ps1 -Objects @('App.obj', 'Dx11Renderer.obj') `
  -RustLibrary 'path/to/wgpu_uwp_probe.lib' -Output 'path/to/probe.exe'
```

This procedure is not an implementation of the prohibited functions. It
fails if the linked program actually retains a sentinel. A release x64 wgpu
probe built using the patched std linked this way and presented a clear frame
in a UWP CoreWindow through ANGLE and D3D11. It is not the Horizon game;
other code paths can require desktop APIs and must be ported individually.

## Runtime regression probe

The `fs-probe` Rust static library checks environment, process, filesystem,
and synchronization behavior. Build it with the patched source and x64 UWP
target, then run `cmd /c uwp\rust-std\fs-probe\build-host.cmd`. Stage its
`build/HorizonUWP.exe` in a loose UWP package with a distinct package identity
and the usual host manifest assets. Once registered and activated, the package
writes `runtime-probe.txt` in its LocalState directory; zero for both results
means the checks passed. The test creates and removes its own temporary files.
