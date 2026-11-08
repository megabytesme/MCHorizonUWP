The Horizon release page shows the exact upstream commit included in the selected patch and its nightly name when it matches a known nightly.

Native mod builds now inspect available releases against the selected Horizon commit and native-hook API, choose compatible older versions where available, and show the exact mod versions in build details. The builder keeps Horizon’s hash, signature and manifest verification in the install path. The mod picker text now wraps within the dialog and its alphabetical index can scroll without cutting off groups.

The finished page can upgrade an installed package of the same architecture. New package versions are chosen above the installed version when necessary; installing a different architecture keeps the separate install action and warns about possible data loss. Release notes now contain only changes for that release.

Extract the ZIP and run Horizon.Builder.App.exe. See README.md and THIRD_PARTY_NOTICES.md for project and dependency information.
