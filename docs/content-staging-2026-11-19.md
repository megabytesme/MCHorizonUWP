# UWP content staging on Nightly 2026-11-18

## Where the time went

The x64 Nightly 1.0.0.4 AppContainer log and its completed mod cache show approximately 146 seconds between creation of the staging directory and materialization of a late vanilla table, followed by about 99 seconds to the final cache stamp. The UI bundle reported 248.3 seconds. File timestamps bound these phases and do not isolate CPU time or individual I/O operations. The completed root held 28,189 files and 624,005,914 bytes, including 4,961 vanilla sound files totaling 376,664,246 bytes.

Import stores the `.hmod` archive in the game folder. Content startup reads its declarations and opens its passive asset and data layers in place. The large write came from `Overlay::new`, which mirrored the prepared tree before registry generation. The UWP Rust standard library rejects hard links, so every vanilla file flowed through the copy fallback. Registry generation then parsed and rewrote each table, including the large block-state facts and collision tables for the entity-only fixture. Those operations precede normal game/world loading. The previous evidence does not quantify archive hashing or world-loading time independently.

## Staging changes

Patch [0.1.6](../patches/nightly-2026-11-18/horizon-uwp-0.1.6.patch) keeps vanilla sound samples in the read-only resource framework. The existing `ResourceSource` supplies those files, the mod's own sound files and the selected resource pack layers to the sound registry and audio engine. Path-based loaders still receive their prepared tables and data. Registry tables scheduled for replacement are generated directly into the staging directory. A mod set without new blocks keeps the original block-state-only tables and avoids decoding gameplay facts during staging. Entity, item and other affected registry tables follow their existing generation rules. ARM32 rewrites one table at a time to bound concurrent table allocations.

The previous cache already held one directory per mod set. The UWP stamp includes the packaged game build ID, resource framework path and manifest, enabled inventory, declared data and SHA-256 of the installed mod archives or loose content inputs. Java dependencies keep their existing content-addressed references and cache identity checks. The existing registered-condition policy builds a fresh generation when native condition handlers can read mutable configuration. Each load composes selected client resource packs above the content cache.

The client and integrated server serialize cache preparation within the AppContainer process. Generation writes the completed stamp into the staging directory before renaming it into place. Failed builds leave the previous published generation available to its readers. A retry of the same generation cleans its interrupted staging directory. Disabling the final mod uses the original framework directly. All staging writes stay in the game folder's existing `.horizon-cache/mod-content` location in LocalState.

The AppContainer log now records elapsed time for input fingerprinting, registry planning, base file materialization and table rewrites, including copied file and byte counts and completed generation reuse.

## Windows desktop measurement

Ben-Desktop, Windows 11 Pro x64 build 26300, Rust 1.99.1 with the MSVC x64 environment. A standalone optimized Rust probe used the production staging-copy code and the installed `Horizon.Asset.26-3.98ccbead789d397639d5badd0bb805` framework. The legacy comparison materialized the complete framework tree except its existing bundle exclusion. The candidate used the entity-only rewrite selection. Each invocation created a fresh output directory under system temp. The runs used the normal Windows filesystem cache.

| Measurement | Full tree | Entity-only layout |
| --- | ---: | ---: |
| Three elapsed copy samples, seconds | 41.330, 36.988, 44.962 | 36.587, 43.111, 29.397 |
| Median copy elapsed, seconds | 41.330 | 36.587 |
| Base files copied per invocation | 28,180 | 23,213 |
| Base bytes copied per invocation | 625,017,619 | 242,683,397 |
| Process read operations | 28,513 | 23,310 |
| Process read bytes | 625,017,619 | 242,683,397 |
| Process write bytes | 625,017,619 | 242,683,397 |
| Peak probe working set, bytes | 6,156,288 | 5,636,096 |

The layout copied 61.2% fewer bytes and 17.6% fewer files. Median elapsed time fell 11.5% with overlapping individual runs. Windows attributed the copied bytes to the process but returned zero write operations, so copied-file count is the available measure for output file creation. The table covers the copy phase and the probe's working set.

An independent desktop debug-profile probe used the original 641-byte `uwp_smoke.hmod` fixture with the exact Nightly's `Package::read` and `Content::read`. Across three fresh directories, the median filesystem import copy took 0.867 ms, opening and reading the package's declarations took 0.874 ms, and SHA-256 took 0.140 ms. The probe found `uwp_smoke:probe` in the archive and verified the enabled and disabled package selection in each directory. This measures the archive operations separately from the Windows picker and the game loader.

## Signed x64 AppContainer trials

The x64 release bridge containing both the native-hook prototype and the content patch passed the strict import link. SignTool verified the installed test APPX `MCHorizonUWP-NativeTest` 1.0.0.1 with SHA-256 `fd7b5176194632db0f7a7d2ee9d08ca84e7664421acd76c118183305244f8861`. Its manifest declares Windows.Universal 10.0.15063.0 and dependencies on the x64 VC runtime, WinUI 2.7 and the exact neutral resource framework named above. The trials held the embedded native hook enabled and used the isolated title only. The runtime logged the physical RTX 3090 Ti at FL9_1 over hardware L11_1 and Fifo presentation. The isolated test configuration muted audio.

| Trial | Active generation and result | Prepared base copy | UI bundle |
| --- | --- | ---: | ---: |
| First content start | New `5954050f8823d949`, registered `uwp_smoke:probe` | 23,213 files, 242,683,397 bytes, 122.298 s | 123.495 s |
| Unchanged restart | Reused `5954050f8823d949` | 0 files | 0.910 s |
| Disabled `.hmod` | Reused native-only `49b9942c89842fb0`, entity absent | 0 files | 0.822 s |
| Re-enabled original | Reused `5954050f8823d949`, entity restored | 0 files | 0.878 s |
| Updated archive | New `aee799d1fc11a2d0`, same entity ID and new `shadow_radius` of 0.65 | 23,213 files, 242,683,397 bytes, 138.453 s | 139.717 s |

All five trials reached the title frame. Fingerprinting the content archive and cache inputs took 3 ms in the cold, warm and updated logs. Registry planning took 115 ms for the first start and 123 ms after the update. The six scheduled table rewrites took 49 ms and 46 ms. Both enabled generations assigned the entity protocol ID 161 and included it in the command registry. The disabled generation omitted it from both. The finished first mod generation held 23,228 files and 248,133,262 bytes. The updated generation held 23,228 files and 248,133,283 bytes. Both keep vanilla audio in the framework. Cold and changed generations still copy the base files required by path-based loaders.

The counters below cover each process from the Start game action through the observed title, including rendering and other resource reads. Peak working set covers the process through that observation. The cold collector stalled while enumerating a Windows path over the legacy limit after saving the startup log. Extended-path verification recovered its completed cache contents. Its whole-process I/O and memory sample was unavailable.

| Trial | Process bytes read | Process bytes written | Peak working set, bytes |
| --- | ---: | ---: | ---: |
| Unchanged | 245,918,040 | 548,721 | 1,264,103,424 |
| Disabled | 251,571,140 | 195,362 | 1,362,657,280 |
| Re-enabled | 248,797,134 | 194,956 | 1,339,146,240 |
| Updated | 509,695,997 | 250,006,738 | 1,196,965,888 |

The isolated process was stopped between runs after saving title evidence. The archive update replaced the scratch copy with the official-format v1.1 fixture and preserved the original file. The v1.0 and v1.1 SHA-256 values were `3a248ded7a147fd746d089c02c21777a093af2180e1e41771cb2d55b31eddf8f` and `795c0567250d9230e04ca82120aa2c91f91c2b47436ae5f82fdd5c7b62aa63c2`. Raw logs and counters are in the local `deliverables/nightly-2026-11-19/content-staging` directory. The historical four-minute startup came from a preceding package. The table records single startup observations on the desktop, and Lumia runtime measurements remain pending.

## Source and cache checks

Four focused Rust tests passed on desktop. They checked preserved table bytes and base audio, table selection for a mod without blocks, cache fingerprint changes after a same-size update with the original timestamp restored, and recovery from a failed generation before publishing a completed retry. The Nightly patch adds a full resource-layer test for resolving both vanilla and mod sound files through an overlay without either file materialized.

The content hunks passed `git apply --check --whitespace=error-all` against the exact source. Builder's `--assemble-candidate` also assembled the pinned upstream ZIP, patch 0.1.6 and the previous 0.1.6 client bundle into a fresh source directory. A reverse-apply check with the Builder's line-ending configuration and comparison of the four resulting Rust files verified the staged content changes. The updated candidate preparation tool also reproduced the original 0.1.5 patch and 0.1.6 client bundle byte for byte.
