# Fresh-PC builder validation (2026-11-06)

The complete clean-PC acceptance test has **not passed**. A fresh Sandbox
resolved dependencies for all four target architectures in about 5 minutes 46
seconds. After Minecraft ownership was confirmed, the builder prepared and
verified the full 26.3 asset cache. Its automatic build stopped at the recipe
guard because the only published client release is too old for the prebuilt-host
recipe. A complete source-to-signed-APPX build and device compatibility remain
unverified. The earlier Visual Studio Build Tools setup took about 35 minutes.

An earlier 2026-11-06 blank Sandbox was started with 8,588,824,576 bytes of
guest memory. Its fresh Windows 11
Enterprise image is version 10.0.26100. The self-contained builder was staged
from a downloaded ZIP and launched as WDAGUtilityAccount. The candidate archive
is `E:\temp\HorizonUWPBuilder-compact-clang-20261106-r2.zip` (SHA-256
`1681F839FB317C45C67ACAE12DAEF540ACDDFAC3DD0644589FA97B82A1532A67`). The
Welcome page in that earlier run was not advanced, so dependency installation
and Minecraft asset preparation did not start. That Sandbox was stopped before
the new resolver run documented below. Microsoft sign-in state was not copied;
asset preparation will require a new sign-in.
At the clean baseline C: had 82,533,052,416 bytes free, and WinGet was absent.
The baseline is saved at
`E:\temp\HorizonCompactSandbox-20261106\host-output\baseline.json` for a
later storage comparison. The new fresh Sandbox run below confirms that the
direct dependency path works without WinGet.

## Component results

- On 2026-11-05 a fresh Sandbox started with 8,588,824,576 bytes of guest
  memory and 83,043,864,576 bytes free on C:. Git, Python, Rustup, Cargo,
  Clang, WinGet and SDK 19041 were absent. That Sandbox has since been reset;
  its result is recorded below.
- An 8192 MB Windows Sandbox opened the self-contained WinUI 3 builder. The
  light theme, architecture default and native-mod search, selection, filtering
  and removal were exercised through UI Automation. Git and Python were
  installed with WinGet in the Sandbox. WinGet was installed there separately
  because the user assumes it is available on a normal PC.
- The user signed in through the Sandbox builder with an account that owns
  Minecraft: Java Edition and confirmed that the ownership check succeeded.
  Asset preparation and the UWP client's own sign-in were not tested there.
- In an empty Sandbox test profile, the builder downloaded the pinned WinUI
  2.7.3 NuGet package, verified its digest, extracted its files, and reused the
  cache on a second call. The Microsoft UI and VC runtime APPXs from the SDK
  were signature and architecture checked for x86, x64 and ARM64. The ARM32
  VC runtime APPX was absent from the development machine's SDK installation.
- The Sandbox also installed LLVM 23.1.4 through the builder's WinGet setup
  path and confirmed that `clang.exe` was present afterwards.
- A later fresh Sandbox started the Visual Studio 2022 Build Tools installation
  through WinGet. Its installer verified the download and progressed into the
  Windows 11 SDK before the host restart interrupted it. No completion result
  or clean-PC build was recorded.
- After the restart, another 8192 MB Sandbox was given Microsoft-signed WinGet
  packages to provide the assumed prerequisite. A harness using the builder's
  all-target Visual Studio setup started the official installer with the x86,
  x64, ARM32 and ARM64 components. The installer verified its bootstrapper;
  setup was stopped so the user could run the builder in a new clean Sandbox;
  this was not a passing toolchain or package-build result.
- In the subsequent user-driven Sandbox run, the builder installed Git,
  Python, private Rustup and Rust 1.99.1 with rust-src automatically, then
  started Visual Studio setup. Python's default installer took several
  minutes installing individual components including its documentation. A
  later local change requests Python without its documentation, tests or Tk
  UI; that faster installation has not yet been verified on a fresh PC.
- In a following Sandbox run, the pinned Visual Studio 2022 Build Tools
  bootstrapper started at 02:55 and completed its C++ installation at 03:33.
  The installer reported success. Its state selected x86/x64, ARM32, ARM64,
  ARM64EC and Clang; BuildTools occupied 8.69 GiB, the Visual Studio package
  cache 3.23 GiB, and the private Windows SDK 1.16 GiB. The install operation
  took about 35 minutes. A later build-time fallback invoked `setup.exe
  modify` with `--wait`; its log explicitly says that `wait` is an unknown
  option and returns exit 87. The bootstrapper-only flag has been removed from
  installer invocations, and the redundant build-time Visual Studio install
  path has been removed. New installs now request `--nocache` to avoid keeping
  the 3.23 GiB package cache. The fresh retry will confirm these changes.
- A reset 8192 MB Sandbox downloaded and verified the Microsoft VC runtime
  framework fallback for all four architectures. This component test used a
  shared copy of Microsoft's SignTool, so it is not an end-to-end clean-PC
  build. The ARM32 package is Microsoft-signed version 14.0.33519.0.
- On the development PC, UI Automation checked that Next on Welcome shows
  the dependency consent dialog and that declining advances to the target page
  with one of six pages marked complete. The dependency expander exposed eight
  separate progress bars. No dependency installer was run in this UI check.
- A strict x64 UWP host link succeeded using a private builder WinUI profile
  instead of the user's NuGet cache; its retained output is in
  `E:\temp\HorizonPrivateProfileHostProof-20261105`.
- The Sandbox verified the public signed catalog and assembled the published
  x64 source release from the upstream archive, patch and client prereleases.
  An earlier download ended with an unexpected EOF; interrupted and stalled
  transfers are now retried and every artifact must pass its SHA-256 check.
- An automated approval review blocked the separate attempt to download and
  launch the published builder ZIP inside Sandbox, reporting only "blocked by
  policy". The wizard checks above used the locally published builder directory.
- An automated approval review also blocked a direct Rustup bootstrap test
  without giving a reason. In the later user-driven Sandbox run, the builder
  installed Rustup, the pinned compiler and rust-src itself and advanced to
  Visual Studio setup.
- Horizon's own registry downloader staged the Zoom native mod. The builder's
  mod pipeline applied it to the UWP source and restored the crate setting
  needed for compilation. An independently staged copy of that same modified
  game source compiled its x64 UWP bridge successfully. Other registry mods
  and modded packages on real devices are not validated.
- Developer-machine proofs produced and signed client APPXs for x64, x86,
  ARM32 and ARM64, using private prepared Minecraft resources. The x64 and x86
  packages installed in Sandbox with their matching Microsoft UI and VC runtime
  frameworks and a neutral resource framework. The ARM32 and ARM64 signatures
  verified in Sandbox; their target architectures cannot be run there. No game
  was launched in these checks.
- A private development-machine test downloaded and checked the pinned 26.3
  resources and used Horizon's preparer to produce resources including sounds.
  The integrated builder helper repeated the download and preparation in
  `E:\temp\HorizonUWPAssetProof-20261105`. The owner confirmed Minecraft
  ownership separately in the Sandbox. This is not an end-to-end Sandbox build.
- The builder's local-install helper installed a separately signed x86 proof
  package and its Microsoft dependencies for the Sandbox user. The test used
  a certificate already trusted in Sandbox; the interactive first-time Windows
  administrator prompt has not been tested.

## Build records

The following records were made on Ben-Desktop under `$env:TEMP`; those
temporary folders are no longer present. They were not release artifacts.
The actual builder workspaces on a user's PC are separate. The builder limits parallel Cargo jobs when
available memory is low, after an ARM32 rebuild hit Windows' paging-file limit.

| Target | Proof workspace | Result |
| --- | --- | --- |
| x64 | `horizon-split-recipe-proof-fcece00382d041cb9f05e30d4b67efa4` | Rust bridge, host, signed client and resource framework built; package installed in Sandbox. |
| x86 | `horizon-x86-sdk-build-20261105` | Rust bridge and strict host built, package signed and installed in Sandbox after adding x86 VC runtime. |
| ARM32 | `horizon-arm-sdk-build-20261105` | Rust bridge and strict host built with VS 2022 and SDK 17763; signed package signature verified in Sandbox. |
| ARM64 | `horizon-arm64-sdk-build-20261105` | `build.cmd` built the Rust bridge; `uwp\host\build-game.cmd arm64` strict-linked the host; signed package signature verified in Sandbox. |
| x64 with Zoom | `horizon-mixins-apply-proof-20261105` | `build-modded.cmd` built the UWP bridge from the transformed source. |

The ARM32 bridge and strict host were also rebuilt with VS 2022 and SDK 19041
in `horizon-vs2022-host-proof-20261105`, limiting Cargo to three jobs after a
32-job attempt exhausted the Windows paging-file limit. This was a developer
machine toolchain check, not a clean-PC package build.

The bridge build uses `cargo +stable build --offline --manifest-path
uwp/game-bridge/Cargo.toml` with the selected target and UWP features, after
fetching dependencies and preparing the patched Rust standard library. ARM32
also uses its offline dependency overrides. The host and package scripts use
Microsoft's SDK tools. These proofs were run on a prepared development machine,
not through a downloaded builder on a blank PC.

## Remaining work

- The catalog build runner now finds `rust-lld.exe` from its pinned Rustup
  toolchain and uses it for the final strict UWP link. The updated path linked
  the real ARM64 host objects and game bridge, passed the forbidden-import and
  8 MiB stack checks, and produced a 47,276,544-byte executable. Builder setup
  tests and the self-contained Release publish also pass. This is a local
  developer-machine check; clean Sandbox use has not been repeated with this
  builder. The direct Microsoft C++ package path below removes the full
  Build Tools installer from dependency setup. Native mods still transform game
  source and rebuild the Rust bridge; that cost needs a fresh-PC measurement.

- The standalone LLVM 23.1.5 archive downloaded for the compact Clang proof was
  276,368,249 bytes and matched its pinned SHA-256. The builder extracts only
  `clang.exe` and the resource headers: 129,219,094 bytes across 323 files,
  instead of installing Visual Studio's LLVM component. The LLVM archive uses a
  1 GiB Zstandard window; extraction now sets that explicit bounded limit. The
  real archive extracted successfully and its Clang compiled ARM32 and ARM64
  COFF objects. The synthetic archive tests, complete setup-checker suite, and
  self-contained Release publish passed. The client still needs Microsoft's
  UWP CRT libraries and Windows SDK for MSVC-ABI Rust targets, but Clang and
  Rust's linker compile and link without `cl.exe`.
- A four-architecture object-kit estimate from the real host objects in the
  scratch proof compressed 157,716,108 bytes of `.obj` files into a 27,647,410
  byte ZIP. The objects have already been strict-linked with their corresponding
  Rust libraries through Rust LLD. The builder now consumes the client-release
  host kit, avoiding host compilation on the user's PC; native mods still cause
  the Rust game bridge to rebuild.

- A compact SDK proof on Ben-Desktop downloaded the five Microsoft SDK
  19041.5 NuGet packages (286.6 MiB total); their pinned SHA-256 digests,
  NuGet SHA-512 metadata, and Microsoft/NuGet package signatures were checked.
  The assembled private SDK occupies about 1.16 GiB. With its headers and
  libraries, the real hosts compiled for all four architectures. The x64
  host linked to an existing real game bridge, and an ARM32 game bridge was
  built and strictly linked to the ARM32 host. A private copy of the local
  MSVC compiler and libraries occupied about 445 MiB and linked both hosts.
  Clean x86 and ARM64 Rust targets were also built and strictly linked using
  the private SDK and compiler layout, with separate x64 host libraries for
  build scripts. The updated repository host scripts also compiled and linked
  all four targets using the explicitly prepared private environment.
  This is a local experiment: it used an existing Rust toolchain, compiler,
  WinUI framework and dependency cache, and did not run the resulting game.
  The ARM32 build used the experimental ureq override which disables its TLS;
  the recipe routes account, skin and Minecraft session requests through a
  WinRT HTTPS adapter, but that adapter and other HTTPS paths still need a
  real device test.
  The scratch directory was `E:\temp\HorizonMinimalSdkTest-20261106`; the
  four `build-repo-host-<architecture>.cmd` commands exited successfully.
  The Rust bridge commands used four Cargo jobs because the Sandbox was active.
- The private SDK's MakeAppx and SignTool also produced and signed synthetic
  APPXs for all four architectures with a 15063 minimum, and the signatures
  were verified. The real ARM32, ARM64, x86 and x64 executables were also
  packaged and signed with verified public certificates, but they used a placeholder
  resource dependency and were not installed or run. These were not complete
  playable packages.
  The compact SDK is now connected to setup and to the build recipe for the
  two pinned clients, using hash-checked script backports in a private source
  workspace. A full clean-PC build and compatibility test is still required.
- The compiler installer fallback has now been replaced by a direct-package
  proof. The builder downloads Microsoft-hosted VSIX packages, verifies their
  SHA-256 digests, and extracts only the needed headers and libraries into its
  per-user toolchain cache. It does not run the Visual Studio installer or
  install the IDE. The dependency dialog links to Microsoft's applicable terms;
  the builder release does not contain these Microsoft packages.
- On Ben-Desktop, the forced direct-package setup fetched and extracted the
  selected toolchain files in 32.6 seconds for x64, then 10.3 seconds for x86,
  6.3 seconds for ARM32, and 20.4 seconds for ARM64. The shared x64 headers and
  host libraries were reused; all four took 69.6 seconds total. The six files
  in the complete architecture set total about 179 MiB compressed. These are
  local network timings, not a clean Sandbox measurement.
- Each resulting MSVC package layout then passed the existing strict UWP bridge
  link with a real Rust game-bridge library, the private SDK 19041, and the
  prepared host kit: x64, x86, ARM32 and ARM64 all produced the expected PE
  architecture with no forbidden-import sentinel hit. This verifies the
  linker inputs for every target; it is not a fresh-source compile or complete
  APPX build.
- A local full dependency-resolution run began with only Rust 1.99.1 and
  `rust-src` missing, because the other tools and SDK were already available or
  cached. It resolved the remaining x64 dependencies in 8.7 seconds. This is a
  warm-machine result, not the clean-PC baseline; the clean Sandbox result is
  recorded below.
- SDK 22000 has an ARM32 NuGet package, but Microsoft's NuGet package sizes for
  its all-architecture set total about 340 MiB, versus 286.6 MiB for the
  currently pinned SDK 19041 set. Both are out of support. Since 19041 has
  already built every target while preserving the 15063 minimum, it remains the
  faster, previously validated choice for this pass.
- The common SDK and x64 architecture packages total roughly 145 MiB. Other
  targets additionally need their architecture package (roughly 47–49 MiB),
  because the Rust build also needs x64 host libraries. If an installed SDK
  already supplies the host files, the builder can reuse them and fetch only
  missing target packages. An ARM64 host was built using that supplemented
  layout. Python.org's 10.9 MB embedded Python 3.13.17 package was also
  SHA-256 checked and successfully ran the real ARM32 preparation script using
  an existing Cargo cache. Setup now prefers a working Python 3.10 or newer,
  then a private copy of that embedded package. MinGit 2.56.0.4 was verified
  and passed the source assembly and patch tests. The user ran Rustup 1.29.3
  directly in a temporary profile and installed Rust 1.99.1 and rust-src; its
  downloaded bootstrapper matched Rust's published SHA-256.
- SDK 19041 is out of Microsoft support. The UWP client and WinUI 2.7 framework
  declare 15063 as their minimum; the separate WinUI 3 builder declares 17763.
  Run the resulting packages on supported devices to validate those minimums.
- Exercise the first dependency download from the Builder UI, including the
  license dialog. The clean-profile resolver proof and the app's cache reuse
  passed, but the UI-triggered first download has not yet been tested.
- Minecraft 26.3 asset preparation after ownership confirmation passed in the
  builder Sandbox. The first attempt had two transient request failures and
  displayed an inaccurate final progress count. Retry reused the 498 MiB
  already cached, fetched the remaining 4 MiB and completed. An independent
  audit verified the client JAR and all 5,147 asset objects: no missing files,
  size errors or SHA-1 mismatches. Fix the progress accounting before relying
  on that display. Generated resources remain local. Test sign-in in the UWP
  client separately.
- Test the local installation offer and first-time certificate approval end to
  end, and complete installation guidance for other devices. Run the client on
  real target hardware, including ARM32.
- Validate more native mods, including different mixin revisions and bundles
  with data or post-build steps. Unsupported cases must continue to fail clearly.
- Run the entire builder flow in a fresh Sandbox, with no preinstalled
  development tools or prepared resources, for every architecture. Sign-in
  requires an account that owns Minecraft: Java Edition.

## Fresh Sandbox dependency resolution (2026-11-06)

- Started a new Windows Sandbox with 8192 MB assigned, using the standard
  `WDAGUtilityAccount` and its clean profile. No development tools were present.
  The builder publish folder was mounted read-only; a self-contained test
  harness ran the same `DependencyProvisioner` used by the app and wrote its
  logs to a separate temporary share. WinGet was absent in the guest after the
  run and was not installed.
- The first x64 resolution found all nine tool/framework groups missing and
  completed in 225.3 seconds. The resolver fetched Git, Python, Rustup and the
  pinned Rust compiler/source, then fetched the compact SDK, Microsoft C++
  link libraries, LLVM, WinUI and x64 VC framework while the parallel tasks
  ran. `MissingAsync` returned no remaining prerequisites.
- With shared tools cached, x86 resolved in 41.6 seconds, ARM32 in 35.2
  seconds and ARM64 in 44.3 seconds. Each target added only its target SDK,
  link-library and runtime-framework pieces. All four dependency checks passed
  in about 5 minutes 46 seconds combined.
- After all four targets were prepared, `LocalAppData\MCHorizonUWP` occupied
  3.82 GiB: 0.77 GiB of retained downloads and a 1.16 GiB assembled SDK, plus
  Rust, LLVM, Git, Python, WinUI and C++ tool files. This is dependency storage
  only; it excludes Minecraft assets, release sources and build outputs.
- The published WinUI 3 app then opened in the same guest as “MCHorizonUWP
  Builder.” Advancing Welcome moved to Architecture and reported 9/10 ready
  for x64, with only Minecraft assets waiting for sign-in. The app recognized
  the dependency cache without downloading it again.
- This verifies clean-profile dependency resolution and the builder's reuse of
  that cache. The first-run dependency download through the UI and its license
  dialog, plus the complete source/build/package/sign flow, remain to be tested.
  The Sandbox is left open for the next interactive steps.
- The release page initially showed no compatible choices because the signed
  catalog had no rule for builder 0.1.3. Commit `c952795` temporarily added a
  rule for client 0.1.1. Automatic build then stopped at the recipe guard: the
  prebuilt-host build requires client 0.1.2, and GitHub has no client 0.1.2
  release. The temporary compatibility rule was withdrawn in `db101ad`. After
  refreshing the signed catalog, the Sandbox again showed no compatible
  release, which is the safe result until the client release with host files is
  available. No fresh-PC APPX was produced.

The original first Sandbox attempt is recorded in Git history. It found missing
WinGet, a missing PRI file and a startup selection problem, which have since
been addressed for the preview; it did not build a package.

## Fresh x64 storage check (2026-11-07)

- Started the released x64 builder in a clean Windows Sandbox configured for
  8192 MB. The guest reported 8,588,824,576 bytes of physical memory and
  85,764,059,136 bytes on C:. The measured free-space baseline was
  82,837,581,824 bytes, after extracting the builder.
- The builder ZIP was 69,846,431 bytes; the extracted app folder was
  175,326,618 bytes. The ZIP itself was provided from a host share and is not
  part of the guest baseline.
- All nine x64 dependency groups were missing. Setup started at 07:22:33 UTC
  and reported ready at 07:26:43 UTC (4 minutes 10 seconds).
- The resulting `%LOCALAPPDATA%\MCHorizonUWP` tree occupied 2,613,320,547
  bytes (2.43 GiB, rounded up to 2.5 GiB), including 568,075,366 bytes of
  retained downloads. The folder breakdown was:

  | Folder | Bytes |
  | --- | ---: |
  | Downloads | 568,075,366 |
  | Git | 86,439,889 |
  | LLVM | 149,875,229 |
  | Python | 22,640,861 |
  | Rust | 705,123,591 |
  | SDK | 686,467,199 |
  | Setup | 29,617,778 |
  | Toolchains | 323,975,651 |
  | ToolProfile | 41,100,701 |
  | Builder logs | 4,282 |

- The asset download and complete build/package run are still pending account
  ownership confirmation. This is not the end-to-end storage requirement.
