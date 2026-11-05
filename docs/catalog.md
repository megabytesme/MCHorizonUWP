# Compatibility catalog

The goal is a signed, versioned release catalog for three independent release
types: `builder-vX.Y.Z`, `client-vX.Y.Z` and `patch-vX.Y.Z`. The initial
catalog implementation is a local parser and compatibility checker. It is
not connected to the UI, does not retrieve artifacts, and does not yet
authenticate catalogs or publish releases. It must not be used to trust or
execute downloaded content until authentication is implemented.

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

Evidence is separate from compatibility. It identifies exact patch, client,
builder and architecture versions, with independent build and device results.
A successful x64 build does not establish ARM32 success or device behavior.
An allowed combination without evidence remains unverified. Release
automation must only publish evidence backed by actual tests.

The first candidate patch applies to the pinned upstream commit. The source
assembler verifies input hashes, applies the patch and overlays a separate UWP
client archive in a new workspace. Synthetic tests and a local assembly of the
first candidate passed. The build runner still uses the old local checkout and
does not yet use this assembly path to build split inputs.
