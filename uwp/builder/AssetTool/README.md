# Private asset preparation helper

The builder embeds this small Rust program and lockfile, then places them in a
new private build workspace alongside the authenticated release source. It
copies Horizon's pinned `launcher/src/mojang.rs` and `sources.rs` into the
helper and links the release's `horizon-prepare` crate. The builder starts this
helper only after Minecraft: Java Edition ownership has been confirmed.

The helper downloads Mojang's files and Java runtime into a private workspace,
verifies the release pins and asset hashes, and asks Horizon to prepare complete
resources. It does not receive account credentials. Minecraft jars, assets and
prepared resources must never be committed or published with this repository.
