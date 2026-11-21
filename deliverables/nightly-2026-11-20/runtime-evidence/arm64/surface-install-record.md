# Surface Pro X ARM64 game candidate

The corrected signed November 20 related set registered on Ben-Tablet at 01:18:27 on November 21, 2026. The interactive task ran in session 1 and returned exit code 0. Both the main and optional packages were native ARM64 at 1.0.1.0 and reported status `Ok`. The existing neutral Horizon 26.3 resource framework remained installed.

| Bundle | Source on Ben-Desktop | Destination on Ben-Tablet | SHA-256 matched after transfer |
| --- | --- | --- | --- |
| Main | `C:\dev\personal\MCHorizonUWP-nightly-2026-11-20\deliverables\nightly-2026-11-20\game\arm64\related\main.appxbundle` | `C:\Users\ben\AppData\Local\Temp\MCHorizonUWP-NightlyNov20-ARM64\main.appxbundle` | `5565C5D0528D2472F6AC40B3C686166129B46E83D099B35C48E415A069E9A689` |
| Optional | `C:\dev\personal\MCHorizonUWP-nightly-2026-11-20\deliverables\nightly-2026-11-20\game\arm64\related\optional.appxbundle` | `C:\Users\ben\AppData\Local\Temp\MCHorizonUWP-NightlyNov20-ARM64\optional.appxbundle` | `BDDFA8B2E4AAE3988BC7D70F6775477824DD69B8156D732BC42E523AD52EE605` |

The isolated identities are `MCHorizonUWP.NightlyNov20.arm64_1.0.1.0_arm64__4pd4jeb6snrqw` and `MCHorizonUWP.NightlyNov20.arm64.NativeMods_1.0.1.0_arm64__4pd4jeb6snrqw`. The transfer used pinned LAN OpenSSH.

A WTS query at 01:19:16 confirmed that session 1 remained locked. Ben needs to unlock the Surface and leave it unlocked through each launch and capture. The staged harness will use a synthetic seed-1234 save and the November 20 Wasm hook fixture. Pending game runs cover projected D3D11 at 67% fixed world scale and DX12 at 100%, with physical renderer evidence and native and Wasm marker screenshots. [Windows session state](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ns-wtsapi32-wtsinfoex_level1_w)
