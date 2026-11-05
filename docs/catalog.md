# Compatibility catalog

The goal is a signed, versioned release catalog for three independent release
types: `builder-vX.Y.Z`, `client-vX.Y.Z` and `patch-vX.Y.Z`. The initial
catalog implementation includes a parser, compatibility checker, P-256
signature verifier, and HTTPS transport that checks artifact digests. These
are not connected to the UI, have no production trust key, and do not publish
releases. They must not be used to trust or execute downloaded content until
the trust key and authenticated distribution are implemented.

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
catalog bytes, using SHA-256 and a 64-byte IEEE P1363 signature. The trusted
public key is supplied as SubjectPublicKeyInfo; there is currently no pinned
production key, key-management workflow, or release signing process. Catalog
URLs and digests are unusable as a trust anchor until those pieces exist.
The development-only `tools/catalog-signer` can sign using an existing P-256
private key in the current user's certificate store, export its public key,
and verify a signed catalog. It does not create, export or back up private
keys. Its `self-test <catalog>` command uses an ephemeral key only.

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
