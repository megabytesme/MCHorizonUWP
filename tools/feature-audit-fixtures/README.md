# Horizon shader audit fixtures

These fixtures exercise one stage at a time against Horizon `0a875da91dbb0fdcb7ba8c42c11341aa229af51a`. Use the same save, camera, output size, scale, clock, weather and renderer for the pack-off capture and each pack run. Compare in SDR first and preserve the two GUI mod markers as independent HUD controls.

| Fixture | Evidence to collect |
| --- | --- |
| `native/audit_sky_marker` | A green patch in unobscured Overworld sky. Terrain and HUD remain outside the sky hook. Recheck a scene with the sky hook ineligible. |
| `native/audit_material_marker` | A magenta shift on opaque terrain faces. For cutouts, compare both inside and outside the existing coverage mask. |
| `native/audit_post_marker` | Large red suppression in the world frame while the HUD marker pixels retain their known colors. |
| `native/audit_multipass` | The producer writes a fixed red/green pair. The consumer paints cyan for a match and orange for a mismatch. Compare the full-screen interior away from HUD and hand overlays. |
| `native/audit_invalid` | SDK refusal at `post.wgsl:2:12`. Use the unpacked directory in a disposable profile for runtime refusal. For live reload, start with a valid pack and then replace its scratch post shader with the invalid shader. Record whether the last good stage stays active. |
| `classic/audit_post_only` | The composite paints a cyan world interior. The fixture requests no gbuffer programs, isolating the post path from classic geometry and entity storage. |
| `native/audit_depth` and `native/audit_depth_load` | Compare reversed-zero sky (blue) and positive-depth geometry (red) using `PostInput.depth` and explicit `depth_at`. |
| `native/audit_entity` and `native/audit_weather` | Green entity marker preserves texture and tint alpha. Rain turns red and snow turns blue with streak alpha preserved. |
| `native/audit_tonemap` | Three achromatic output bands at 0.20, 0.50 and 0.667 in the declared linear output. |
| `native/audit_bt2020` and `native/audit_motion` | SDK-positive probes with plain blue post bodies. Check refusal on a route missing the declared capability. |

The native SDK can check the source directory and each generated archive.

```powershell
& $HorizonShader check tools/feature-audit-fixtures/native/audit_multipass --json
& $HorizonShader pack tools/feature-audit-fixtures/native/audit_multipass --out "$FixtureScratch/audit_multipass.hshader"
& $HorizonShader check "$FixtureScratch/audit_multipass.hshader" --json
& $HorizonShader check tools/feature-audit-fixtures/native/audit_invalid --json
```

`verify_classic.rs` links to the pinned `horizon_shaders` Rust library and asserts exactly one composite vertex/fragment pair writing `colortex0`. Run from the audit checkout using the same Rust toolchain that built the library.

```powershell
rustc --edition 2024 tools/feature-audit-fixtures/verify_classic.rs `
  --extern "horizon_shaders=$HorizonShadersRlib" -L "dependency=$CargoTarget/debug/deps" `
  -o "$FixtureScratch/verify_classic.exe"
& "$FixtureScratch/verify_classic.exe" tools/feature-audit-fixtures/classic/audit_post_only
```

The build used Rust 1.99.1, the Visual Studio Build Tools 14.44.35207 x64 linker and Windows SDK 10.0.19041.0 library paths. SDK and classic parser results establish offline admission and translation. Runtime acceptance requires the named backend, effective mode and pixels. [Offline receipt](../../deliverables/feature-audit/shader-evidence/followup-offline.json)
