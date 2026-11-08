# Compatibility catalog

The signed, versioned release catalog tracks three independent release types:
`builder-vX.Y.Z`, `client-vX.Y.Z` and `patch-vX.Y.Z`. The catalog implementation
includes a parser, compatibility checker, P-256 signature verifier, HTTPS
transport and an embedded public verification key. The builder selects catalog
releases and passes verified inputs to the build runner. Client 0.1.3 and patch
0.1.1 are the current production releases; Builder 0.1.8 is compatible with
that pair on x86, x64, ARM32 and ARM64. Client 0.1.2 remains listed so the
Builder can identify it as incompatible; its prebuilt hosts omit the WinMD
required to activate the UWP application.

Schema 1 uses three-part numeric stable versions. A rule has inclusive lower
and exclusive upper bounds for patch, client and builder versions, and an
explicit list of `x86`, `x64`, `arm` and/or `arm64` targets. Each patch pins
an exact 40-hex upstream Horizon commit and a numbered bridge interface; each
client declares its bridge interface. A combination is allowed only when
both bridge numbers match and one rule matches all four dimensions.
The current builder recipe requires client 0.1.3, which packages the WinMD
activation metadata and prebuilt host files for all four architectures, and
patch 0.1.1, which pins the matching Horizon nightly. The signed catalog
permits Builder 0.1.8 to assemble that pair. The release notes distinguish
compatibility from per-architecture build or device evidence.

Artifacts have HTTPS URLs and SHA-256 digests. Catalog authenticity is a
separate required check: a hash from an unauthenticated catalog does not
establish the publisher. Reject unknown schemas, malformed versions,
duplicate release versions or JSON properties, invalid ranges, invalid
artifact references and conflicting evidence. Patch artifacts use immutable
`raw.githubusercontent.com` commit URLs. Client release artifacts use the
GitHub Releases API asset endpoint, with `Accept: application/octet-stream` and
`X-GitHub-Api-Version: 2022-11-28`; this avoids a direct release-download URL
that returned HTTP 504 from Windows Sandbox. Pass `--client-asset-id` to
`tools/prepare_candidate.py` after uploading a client archive to its GitHub
release so the generated catalog uses that API endpoint.

The verifier accepts a detached P-256 ECDSA signature over the exact UTF-8
catalog bytes, using SHA-256 and a 64-byte IEEE P1363 signature. The builder
embeds the public SubjectPublicKeyInfo key in `uwp/builder/Core/Assets/`.
`catalog/catalog-v1.json` and its detached `.sig` contain the current release catalog.
The catalog declares x86, x64, ARM32 and ARM64 combinations and records no
end-to-end builder or device evidence.

The maintainer's P-256 private key is in the current Windows user's personal
certificate store, with friendly name `MCHorizonUWP release catalog signing`.
It is exportable only through an encrypted private-key export; no private key
is stored in this repository. Back up the certificate and private key as an
encrypted PFX in a secure location before relying on this key for releases.
If it is lost, a builder update with a new public key will be necessary. The
`tools/catalog-signer` signs with a certificate-store thumbprint, exports only
the public key, and verifies catalogs. Its `self-test` uses an ephemeral key.
Catalog and signature must be updated together; a mismatch is rejected.
Schema 1 has no catalog sequence or rollback protection, so replaying an old
validly signed catalog is not yet detected. Resolve this before relying on the
catalog to retire unsafe releases.

Evidence is separate from compatibility. It identifies exact patch, client,
builder and architecture versions, with independent build and device results.
A successful x64 build does not establish ARM32 success or device behavior.
An allowed combination without evidence remains unverified. Release
automation must only publish evidence backed by actual tests.

The first candidate patch applies to the pinned upstream commit. The source
assembler verifies input hashes, applies the patch and overlays a separate UWP
client archive in a new workspace. Synthetic tests and a local assembly of the
first candidate passed. The Sandbox verified the public catalog and assembled
the x64 source release. Development-machine proofs produced signed packages
for all four targets, and x64 and x86 packages were installed in Sandbox. The
complete builder flow on a fresh PC and runtime behavior on target devices
remain unverified.

## Where changes belong

- Builder screens, orchestration and dependency setup belong in `uwp/builder/`.
- UWP client code belongs in `uwp/`: the native shell is in `uwp/host/`, the
  Rust client bridge is in `uwp/game-bridge/`, and platform adapters live in
  their named directories beside them. Client changes are committed normally
  and published together as a versioned client release archive.
- Changes to Horizon itself belong in the Horizon source history when they are
  suitable upstream. UWP-specific changes that must be applied to an upstream
  nightly are ordinary commits on top of that exact upstream commit, exported
  as a Git patch, and checked into `patches/<nightly>/`. The matching
  `patch-vX.Y.Z` release carries the patch file. The builder verifies the
  upstream commit and artifact hashes, checks the patch applies cleanly, then
  applies it in a private build workspace; it never edits the user's checkout.
- `catalog/catalog-v1.json` records each patch/client version, its artifact
  hashes and the compatible version ranges. The detached signature must be
  refreshed after catalog edits. A patch release and a client release are
  separate: updating one does not require copying the other's source into it.

The patch is generated from Git history rather than handwritten. Keep a clean
checkout of the pinned upstream revision, commit the UWP-specific Horizon
changes on top, then export that commit with `git format-patch` (or an equivalent
binary-safe Git patch command). Before release, apply it to a fresh archive of
the pinned commit and verify the resulting source. Client changes do not go in
that patch: they stay in this repository's `uwp/` tree and client release.

Client 0.1.3 carries four architecture-specific C++/CX host executables, their
activation WinMD metadata, import libraries and a compact link kit. Client
0.1.2 omitted the WinMD and could be installed but not activated. The builder
compiles the selected Horizon patch and mods as a Rust static library, then
links that library into a bridge DLL with Rust's bundled `rust-lld`. The APPX
contains the matching host executable and bridge DLL. This skips compiling the
Windows host on a user's PC; the builder still prepares Rust, Clang, the
Windows SDK and Microsoft app frameworks when they are unavailable. Setup
links the component license terms.

The fresh-PC flow and runtime behavior for all four architectures remain under
validation. Compatibility rules describe which versions a builder recognizes;
build support and device testing are recorded separately as evidence.
