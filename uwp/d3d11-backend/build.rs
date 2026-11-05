use std::{env, path::PathBuf, process::Command};

fn main() {
    println!("cargo:rerun-if-changed=terrain.hlsl");
    println!("cargo:rerun-if-env-changed=WindowsSdkDir");
    println!("cargo:rerun-if-env-changed=WindowsSDKVersion");
    if env::var_os("CARGO_CFG_WINDOWS").is_none() {
        return;
    }
    let sdk =
        PathBuf::from(env::var_os("WindowsSdkDir").expect("run in an MSVC developer environment"));
    let version = env::var("WindowsSDKVersion").expect("Windows SDK version is not set");
    let fxc = sdk
        .join("bin")
        .join(version.trim_end_matches(['\\', '/']))
        .join("x64/fxc.exe");
    let out = PathBuf::from(env::var_os("OUT_DIR").unwrap());
    for (entry, profile, file) in [
        ("VSMain", "vs_4_0_level_9_1", "terrain-vs.cso"),
        ("PSMain", "ps_4_0_level_9_1", "terrain-ps.cso"),
        ("PSOpaque", "ps_4_0_level_9_1", "terrain-opaque-ps.cso"),
        ("PSEntity", "ps_4_0_level_9_1", "entity-ps.cso"),
        ("PSAlpha", "ps_4_0_level_9_1", "terrain-alpha-ps.cso"),
        ("PSInvert", "ps_4_0_level_9_1", "terrain-invert-ps.cso"),
    ] {
        let status = Command::new(&fxc)
            .args(["/nologo", "/Zpr", "/T", profile, "/E", entry, "/Fo"])
            .arg(out.join(file))
            .arg("terrain.hlsl")
            .status()
            .expect("could not run fxc");
        assert!(status.success(), "{entry} shader compilation failed");
    }
}
