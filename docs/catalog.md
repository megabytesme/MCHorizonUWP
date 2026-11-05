# Compatibility catalog

The goal is a signed, versioned release catalog for three independent release
types: `builder-vX.Y.Z`, `client-vX.Y.Z` and `patch-vX.Y.Z`. The catalog
implementation includes a parser, compatibility checker, P-256 signature
verifier, HTTPS transport and an embedded public verification key. The wizard
does not yet use the catalog to download or build releases, and the first
client and patch releases are still drafts. They are not a supported release.

Schema 1 uses three-part numeric stable versions. A rule has inclusive lower
and exclusive upper bounds for patch, client and builder versions, and an
explicit list of `x86`, `x64`, `arm` and/or `arm64` targets. Each patch pins
an exact 40-hex upstream Horizon commit and a numbered bridge interface; each
client declares its bridge interface. A combination is allowed only when
both bridge numbers match and one rule matches all four dimensions.

Artifacts have HTTPS URLs and SHA-256 digests. Catalog authenticity is a
separate required check: a hash from an unauthenticated catalog does not
establish the publisher. Reject unknown schemas, malformed versions,
duplicate release versions or JSON properties, invalid ranges, invalid
artifact references and conflicting evidence.

The verifier accepts a detached P-256 ECDSA signature over the exact UTF-8
catalog bytes, using SHA-256 and a 64-byte IEEE P1363 signature. The builder
embeds the public SubjectPublicKeyInfo key in `uwp/builder/Core/Assets/`.
`catalog/catalog-v1.json` and its detached `.sig` contain the initial candidate;
their client and patch download URLs will not work until those draft releases
are published. The candidate currently declares only x64 and records no build
or device evidence.

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
first candidate passed. A development runner can now build and sign that exact
split-source candidate; an x64 output and its separate resource framework were
verified and installed in Sandbox. The wizard still uses the old local
checkout, and authenticated public releases and the other target builds remain
unverified.
