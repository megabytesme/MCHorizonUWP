mod mojang;
mod sources;
mod process {
    pub fn command(program: impl AsRef<std::ffi::OsStr>) -> std::process::Command {
        let mut command = std::process::Command::new(program);
        #[cfg(windows)] { use std::os::windows::process::CommandExt; command.creation_flags(0x08000000); }
        command
    }
}
use std::{fs, io::Read, path::PathBuf, sync::atomic::AtomicBool, time::Duration};
use serde_json::Value;

fn checked_text(v: &Value, key: &str) -> Result<String, String> {
    v.get(key).and_then(Value::as_str).map(str::to_owned).ok_or_else(|| format!("Missing {key} in Mojang metadata"))
}
fn allowed_url(text: &str) -> bool {
    let Ok(url) = url::Url::parse(text) else { return false; };
    let allowed = ["piston-meta.mojang.com", "piston-data.mojang.com", "resources.download.minecraft.net", "libraries.minecraft.net", "launchermeta.mojang.com"];
    url.scheme() == "https" && url.port_or_known_default() == Some(443) && url.username().is_empty()
        && url.password().is_none() && url.host_str().is_some_and(|host| allowed.contains(&host))
}
fn run() -> Result<(), String> {
    let mut args = std::env::args_os().skip(1);
    let source = PathBuf::from(args.next().ok_or("Missing authenticated source directory")?);
    let private = PathBuf::from(args.next().ok_or("Missing private download directory")?);
    if args.next().is_some() || !source.join("research-lock.json").is_file()
        || (private.exists() && (!private.is_dir() || fs::symlink_metadata(&private).map(|m| m.file_type().is_symlink()).unwrap_or(true)))
    { return Err("Invalid source or private workspace".into()); }
    fs::create_dir_all(&private).map_err(|e| e.to_string())?;
    let lock: Value = serde_json::from_slice(&fs::read(source.join("research-lock.json")).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
    let pins = sources::Pins::read(&lock).ok_or("The release has no Minecraft pins")?;
    if pins.version != "26.3" { return Err("This builder does not support the pinned Minecraft version".into()); }
    let agent = ureq::AgentBuilder::new().redirects(0).timeout_connect(Duration::from_secs(30)).timeout_read(Duration::from_secs(90)).build();
    let fetch = |text: &str| -> Result<Vec<u8>, String> {
        if !allowed_url(text) { return Err("Mojang metadata contained an untrusted URL".into()); }
        let mut failure = String::new();
        for attempt in 0..3 {
            match agent.get(text).call() {
                Ok(response) => {
                    let mut bytes = Vec::new();
                    match response.into_reader().take(256 * 1024 * 1024 + 1).read_to_end(&mut bytes) {
                        Ok(_) if bytes.len() <= 256 * 1024 * 1024 => return Ok(bytes),
                        Ok(_) => return Err("A Mojang download exceeded its size limit".into()),
                        Err(error) => failure = error.to_string(),
                    }
                }
                Err(error) => failure = error.to_string(),
            }
            if attempt < 2 { std::thread::sleep(Duration::from_secs(attempt + 1)); }
        }
        Err(format!("Could not download a Mojang file: {failure}"))
    };
    let log = |s: String| println!("{s}");
    let game = private.join("minecraft");
    let cancel = AtomicBool::new(false);
    mojang::download(&pins, &game, "windows", 8, &fetch, &cancel, &log)?;
    let manifest_path = game.join("versions").join(&pins.version).join(format!("{}.json", pins.version));
    let manifest: Value = serde_json::from_slice(&fs::read(&manifest_path).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
    let index = &manifest["assetIndex"];
    let index_path = game.join("assets/indexes").join(format!("{}.json", checked_text(index,"id")?));
    let expected_index = checked_text(index,"sha1")?;
    if sources::sha1_file(&index_path).as_deref() != Some(&expected_index) { return Err("Minecraft asset index did not verify".into()); }
    let assets: Value = serde_json::from_slice(&fs::read(index_path).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
    let objects = assets["objects"].as_object().ok_or("Missing Minecraft asset objects")?;
    for entry in objects.values() {
        let hash = checked_text(entry,"hash")?;
        if hash.len() != 40 || !hash.bytes().all(|b| b.is_ascii_hexdigit()) { return Err("Invalid Minecraft asset hash".into()); }
        let path = game.join("assets/objects").join(&hash[..2]).join(&hash);
        if sources::sha1_file(&path).as_deref() != Some(&hash) { return Err("Minecraft assets were not completely downloaded; retry".into()); }
    }
    let server = &manifest["downloads"]["server"];
    let sha = checked_text(&lock["minecraft"], "server_sha1")?;
    if checked_text(server,"sha1")? != sha { return Err("The Minecraft server does not match the release pin".into()); }
    let server_path = private.join("server.jar");
    if sources::sha1_file(&server_path).as_deref() != Some(sha.as_str()) {
        println!("Downloading the Minecraft server data");
        let server_bytes = sources::fetch_checked(&fetch, &checked_text(server,"url")?, &sha)?;
        sources::write_file(&server_path, &server_bytes)?;
    } else {
        println!("Reusing verified Minecraft server data");
    }
    let component = checked_text(&manifest["javaVersion"],"component")?;
    let java = mojang::java_runtime(&component, &private.join("runtime"), 8, &fetch, &cancel, &log)?;
    println!("Preparing Minecraft resources with Horizon");
    let options = horizon_prepare::Options { root: source.clone(), import: true, minecraft_dir: Some(game), server_jar: Some(server_path), java_home: Some(java), inputs: Default::default(), output: None };
    let output = horizon_prepare::prepare(&options, &|s| println!("{s}"))?;
    horizon_prepare::add_assets("sounds", &options.root, Some(&output), options.minecraft_dir.as_deref(), &options.inputs, &|s| println!("{s}"))?;
    let lacking = horizon_prepare::check(&options.root, &output)?;
    if !lacking.is_empty() { return Err(format!("Resources are incomplete: {}", lacking.join("; "))); }
    println!("READY {}", output.display());
    Ok(())
}
fn main() {
    if std::env::args().nth(1).as_deref() == Some("--self-check") {
        assert!(allowed_url("https://piston-data.mojang.com/example"));
        assert!(!allowed_url("http://piston-data.mojang.com/example"));
        assert!(!allowed_url("https://piston-data.mojang.com.evil.example/file"));
        assert!(!allowed_url("https://user@piston-data.mojang.com/file"));
        assert!(!allowed_url("https://piston-data.mojang.com:8443/file"));
        println!("Mojang URL validation passed.");
        return;
    }
    if let Err(error) = run() { eprintln!("{error}"); std::process::exit(1); }
}
