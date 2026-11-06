//! Quiet, read-only release checks for catalog-built UWP packages.
//!
//! The installed release identity is stamped by the builder. The check uses a
//! maintainer-signed client catalog and public Horizon release metadata only;
//! it never sends account details or installs an update.

use std::path::Path;
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
use std::path::PathBuf;
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
use std::sync::mpsc::{self, Receiver};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

const CATALOG_URL: &str =
    "https://raw.githubusercontent.com/megabytesme/MCHorizonUWP/main/catalog/catalog-v1.json";
const SIGNATURE_URL: &str =
    "https://raw.githubusercontent.com/megabytesme/MCHorizonUWP/main/catalog/catalog-v1.sig";
const HORIZON_RELEASES_URL: &str =
    "https://api.github.com/repos/horizon-rust/horizon/releases?per_page=100";
const CLIENT_RELEASE_BASE: &str =
    "https://github.com/megabytesme/MCHorizonUWP/releases/tag/client-v";
const HORIZON_RELEASE_BASE: &str = "https://github.com/horizon-rust/horizon/releases/tag/";
const GITHUB_API_BASE: &str = "https://api.github.com/repos/horizon-rust/horizon";
const CATALOG_LIMIT: usize = 2_000_000;
const RELEASES_LIMIT: usize = 512_000;
const COMPARE_LIMIT: usize = 64_000;
const CACHE_MAX_BYTES: u64 = 32_768;
const CACHE_TTL: Duration = Duration::from_secs(6 * 60 * 60);
const STALE_CACHE_TTL: Duration = Duration::from_secs(24 * 60 * 60);
const PUBLIC_CATALOG_KEY_DER: &[u8] = include_bytes!("../assets/catalog-public.der");

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstalledRelease {
    pub patch_version: String,
    pub client_version: String,
    pub upstream_commit: String,
}

impl InstalledRelease {
    /// Parses `HorizonRelease.json` from the APPX root.
    pub fn parse_json(bytes: &[u8]) -> Option<Self> {
        if bytes.len() > 4096 {
            return None;
        }
        let value: serde_json::Value = serde_json::from_slice(bytes).ok()?;
        if value.get("schema_version")?.as_u64()? != 1 {
            return None;
        }
        let patch_version = value.get("patch_version")?.as_str()?.to_owned();
        let client_version = value.get("client_version")?.as_str()?.to_owned();
        let upstream_commit = value.get("upstream_commit")?.as_str()?.to_ascii_lowercase();
        parse_version(&patch_version)?;
        parse_version(&client_version)?;
        if !is_commit(&upstream_commit) {
            return None;
        }
        Some(Self {
            patch_version,
            client_version,
            upstream_commit,
        })
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UpdateNotice {
    pub message: String,
    pub stable_game_tag: Option<String>,
    pub nightly_game_tag: Option<String>,
    pub client_version: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
struct NoticeParts {
    stable_game_tag: Option<String>,
    nightly_game_tag: Option<String>,
    client_version: Option<String>,
}

impl NoticeParts {
    fn notice(self) -> Option<UpdateNotice> {
        if self.stable_game_tag.is_none()
            && self.nightly_game_tag.is_none()
            && self.client_version.is_none()
        {
            return None;
        }
        let mut lines = Vec::new();
        if let Some(tag) = &self.stable_game_tag {
            lines.push(format!(
                "A newer Horizon stable release is available: {tag} ({HORIZON_RELEASE_BASE}{tag})."
            ));
        }
        if let Some(tag) = &self.nightly_game_tag {
            lines.push(format!(
                "A newer Horizon nightly is available: {tag} ({HORIZON_RELEASE_BASE}{tag})."
            ));
        }
        if let Some(version) = &self.client_version {
            lines.push(format!("A newer MCHorizonUWP client is available: {version} ({CLIENT_RELEASE_BASE}{version})."));
        }
        Some(UpdateNotice {
            message: lines.join("\n"),
            stable_game_tag: self.stable_game_tag,
            nightly_game_tag: self.nightly_game_tag,
            client_version: self.client_version,
        })
    }
}

#[derive(Debug)]
struct Release {
    tag: String,
    commit: String,
}

#[derive(Debug)]
struct CacheRecord {
    notice: NoticeParts,
}

#[derive(Debug)]
struct CheckResult {
    parts: NoticeParts,
    complete: bool,
}

/// Starts a background, read-only release check. `None` means the check is
/// disabled or this package does not contain valid release metadata.
///
/// The caller should poll the returned receiver from its normal UI pump. A
/// completed check sends `None` when there is no newer release or the network
/// is unavailable.
#[cfg(all(target_vendor = "uwp", feature = "xaml-shell"))]
pub fn spawn(
    enabled: bool,
    installed: Option<InstalledRelease>,
    cache_path: PathBuf,
) -> Option<Receiver<Option<UpdateNotice>>> {
    if !enabled {
        return None;
    }
    let installed = installed?;
    let (sender, receiver) = mpsc::channel();
    std::thread::spawn(move || {
        let notice = check_with_cache(&installed, &cache_path, |url, limit| {
            horizon_uwp_https_bridge::get(url, limit)
        });
        let _ = sender.send(notice);
    });
    Some(receiver)
}

fn check_with_cache<F>(
    installed: &InstalledRelease,
    cache_path: &Path,
    fetch: F,
) -> Option<UpdateNotice>
where
    F: FnMut(&str, usize) -> Option<Vec<u8>>,
{
    let now = unix_now()?;
    if let Some(cached) = load_cache(cache_path, installed, now, CACHE_TTL) {
        return cached.notice.notice();
    }
    let result = perform_check(installed, fetch);
    if result.complete {
        save_cache(cache_path, installed, &result.parts, now);
    }
    let notice = result.parts.notice();
    if result.complete || notice.is_some() {
        notice
    } else {
        load_cache(cache_path, installed, now, STALE_CACHE_TTL)
            .and_then(|entry| entry.notice.notice())
    }
}

fn perform_check<F>(installed: &InstalledRelease, mut fetch: F) -> CheckResult
where
    F: FnMut(&str, usize) -> Option<Vec<u8>>,
{
    let mut complete = true;
    let client_update = match fetch(CATALOG_URL, CATALOG_LIMIT)
        .and_then(|catalog| fetch(SIGNATURE_URL, 64).map(|signature| (catalog, signature)))
        .and_then(|(catalog, signature)| verify_and_parse_catalog(&catalog, &signature).ok())
        .and_then(|clients| {
            clients
                .into_iter()
                .max_by(|left, right| compare_versions(left, right))
        }) {
        Some(newest_client) => (compare_versions(&newest_client, &installed.client_version)
            .is_gt())
        .then_some(newest_client),
        None => {
            complete = false;
            None
        }
    };

    let game_releases = fetch(HORIZON_RELEASES_URL, RELEASES_LIMIT)
        .and_then(|bytes| select_game_releases(&bytes).ok());
    if game_releases.is_none() {
        complete = false;
    }
    let mut ahead_cache = std::collections::HashMap::<String, bool>::new();
    let (stable_game_tag, nightly_game_tag) = if let Some((stable, nightly)) = game_releases {
        let stable = match release_is_ahead(
            stable.as_ref(),
            &installed.upstream_commit,
            &mut ahead_cache,
            &mut fetch,
        ) {
            Ok(tag) => tag,
            Err(()) => {
                complete = false;
                None
            }
        };
        let nightly = match release_is_ahead(
            nightly.as_ref(),
            &installed.upstream_commit,
            &mut ahead_cache,
            &mut fetch,
        ) {
            Ok(tag) => tag,
            Err(()) => {
                complete = false;
                None
            }
        };
        (stable, nightly)
    } else {
        (None, None)
    };
    CheckResult {
        parts: NoticeParts {
            stable_game_tag,
            nightly_game_tag,
            client_version: client_update,
        },
        complete,
    }
}

fn release_is_ahead<F>(
    release: Option<&Release>,
    installed_commit: &str,
    ahead_cache: &mut std::collections::HashMap<String, bool>,
    fetch: &mut F,
) -> Result<Option<String>, ()>
where
    F: FnMut(&str, usize) -> Option<Vec<u8>>,
{
    let Some(release) = release else {
        return Ok(None);
    };
    if release.commit == installed_commit {
        return Ok(None);
    }
    let ahead = if let Some(value) = ahead_cache.get(&release.commit) {
        *value
    } else {
        let url = format!(
            "{GITHUB_API_BASE}/compare/{installed_commit}...{}",
            release.commit
        );
        let response = fetch(&url, COMPARE_LIMIT).ok_or(())?;
        let value = compare_is_ahead(&response)?;
        ahead_cache.insert(release.commit.clone(), value);
        value
    };
    Ok(ahead.then(|| release.tag.clone()))
}

fn verify_and_parse_catalog(bytes: &[u8], signature_bytes: &[u8]) -> Result<Vec<String>, ()> {
    if bytes.len() > CATALOG_LIMIT || signature_bytes.len() != 64 {
        return Err(());
    }
    use p256::ecdsa::{Signature, VerifyingKey, signature::Verifier};
    use p256::pkcs8::DecodePublicKey;
    let key = VerifyingKey::from_public_key_der(PUBLIC_CATALOG_KEY_DER).map_err(|_| ())?;
    let signature = Signature::from_slice(signature_bytes).map_err(|_| ())?;
    key.verify(bytes, &signature).map_err(|_| ())?;

    let catalog: serde_json::Value = serde_json::from_slice(bytes).map_err(|_| ())?;
    if catalog
        .get("schema_version")
        .and_then(serde_json::Value::as_u64)
        != Some(1)
    {
        return Err(());
    }
    let clients = catalog
        .get("clients")
        .and_then(serde_json::Value::as_array)
        .ok_or(())?;
    let mut versions = Vec::with_capacity(clients.len());
    for client in clients {
        let version = client
            .get("version")
            .and_then(serde_json::Value::as_str)
            .ok_or(())?
            .to_owned();
        parse_version(&version).ok_or(())?;
        versions.push(version);
    }
    if versions.is_empty() {
        return Err(());
    }
    Ok(versions)
}

fn select_game_releases(bytes: &[u8]) -> Result<(Option<Release>, Option<Release>), ()> {
    let value: serde_json::Value = serde_json::from_slice(bytes).map_err(|_| ())?;
    let entries = value.as_array().ok_or(())?;
    let mut stable: Option<(String, Release)> = None;
    let mut nightly: Option<(String, String, Release)> = None;
    for entry in entries {
        if entry.get("draft").and_then(serde_json::Value::as_bool) != Some(false) {
            continue;
        }
        let Some(tag) = entry.get("tag_name").and_then(serde_json::Value::as_str) else {
            continue;
        };
        let Some(commit) = entry
            .get("target_commitish")
            .and_then(serde_json::Value::as_str)
        else {
            continue;
        };
        let commit = commit.to_ascii_lowercase();
        if !is_commit(&commit) {
            continue;
        }
        let published_at = entry
            .get("published_at")
            .and_then(serde_json::Value::as_str)
            .unwrap_or("")
            .to_owned();
        let release = Release {
            tag: tag.to_owned(),
            commit,
        };
        if let Some(version) = tag
            .strip_prefix('v')
            .filter(|version| parse_version(version).is_some())
        {
            let key = version.to_owned();
            if stable
                .as_ref()
                .is_none_or(|(current, _)| compare_versions(&key, current).is_gt())
            {
                stable = Some((key, release));
            }
        } else if is_nightly_tag(tag) {
            // Date-suffixed tags sort chronologically; `nightly` falls back to
            // GitHub's publication timestamp and remains a valid old-style tag.
            let date = tag.strip_prefix("nightly-").unwrap_or("").to_owned();
            let order = if date.is_empty() {
                published_at.clone()
            } else {
                date.clone()
            };
            if nightly
                .as_ref()
                .is_none_or(|(current_order, current_published, _)| {
                    (&order, &published_at) > (current_order, current_published)
                })
            {
                nightly = Some((order, published_at, release));
            }
        }
    }
    Ok((
        stable.map(|(_, release)| release),
        nightly.map(|(_, _, release)| release),
    ))
}

fn compare_is_ahead(bytes: &[u8]) -> Result<bool, ()> {
    let value: serde_json::Value = serde_json::from_slice(bytes).map_err(|_| ())?;
    let status = value
        .get("status")
        .and_then(serde_json::Value::as_str)
        .ok_or(())?;
    let ahead_by = value
        .get("ahead_by")
        .and_then(serde_json::Value::as_u64)
        .ok_or(())?;
    Ok(status == "ahead" && ahead_by > 0)
}

fn is_nightly_tag(tag: &str) -> bool {
    if tag == "nightly" {
        return true;
    }
    let Some(date) = tag.strip_prefix("nightly-") else {
        return false;
    };
    let mut parts = date.split('-');
    let (Some(year), Some(month), Some(day), None) =
        (parts.next(), parts.next(), parts.next(), parts.next())
    else {
        return false;
    };
    year.len() == 4
        && month.len() == 2
        && day.len() == 2
        && year.bytes().all(|byte| byte.is_ascii_digit())
        && month.bytes().all(|byte| byte.is_ascii_digit())
        && day.bytes().all(|byte| byte.is_ascii_digit())
}

fn parse_version(value: &str) -> Option<(u64, u64, u64)> {
    let mut parts = value.split('.');
    let (Some(major), Some(minor), Some(patch), None) =
        (parts.next(), parts.next(), parts.next(), parts.next())
    else {
        return None;
    };
    fn number(part: &str) -> Option<u64> {
        if part.is_empty()
            || (part.len() > 1 && part.starts_with('0'))
            || !part.bytes().all(|byte| byte.is_ascii_digit())
        {
            None
        } else {
            part.parse().ok()
        }
    }
    Some((number(major)?, number(minor)?, number(patch)?))
}

fn compare_versions(left: &str, right: &str) -> std::cmp::Ordering {
    match (parse_version(left), parse_version(right)) {
        (Some(left), Some(right)) => left.cmp(&right),
        _ => std::cmp::Ordering::Equal,
    }
}

fn is_commit(value: &str) -> bool {
    value.len() == 40 && value.bytes().all(|byte| byte.is_ascii_hexdigit())
}

fn unix_now() -> Option<u64> {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .ok()
        .map(|time| time.as_secs())
}

fn load_cache(
    path: &Path,
    installed: &InstalledRelease,
    now: u64,
    max_age: Duration,
) -> Option<CacheRecord> {
    let metadata = std::fs::metadata(path).ok()?;
    if metadata.len() > CACHE_MAX_BYTES {
        return None;
    }
    let bytes = std::fs::read(path).ok()?;
    let value: serde_json::Value = serde_json::from_slice(&bytes).ok()?;
    if value
        .get("schema_version")
        .and_then(serde_json::Value::as_u64)
        != Some(1)
    {
        return None;
    }
    let checked_at = value
        .get("checked_at")
        .and_then(serde_json::Value::as_u64)?;
    if now < checked_at || now - checked_at > max_age.as_secs() {
        return None;
    }
    let cached_installed = InstalledRelease {
        patch_version: value.get("patch_version")?.as_str()?.to_owned(),
        client_version: value.get("client_version")?.as_str()?.to_owned(),
        upstream_commit: value.get("upstream_commit")?.as_str()?.to_ascii_lowercase(),
    };
    if &cached_installed != installed {
        return None;
    }
    let notice = parse_cached_notice(value.get("notice")?)?;
    Some(CacheRecord { notice })
}

fn parse_cached_notice(value: &serde_json::Value) -> Option<NoticeParts> {
    if !value.is_object() {
        return None;
    }
    let stable_game_tag = optional_string(value, "stable_game_tag")?;
    let nightly_game_tag = optional_string(value, "nightly_game_tag")?;
    let client_version = optional_string(value, "client_version")?;
    if stable_game_tag
        .as_deref()
        .is_some_and(|tag| !is_stable_tag(tag))
        || nightly_game_tag
            .as_deref()
            .is_some_and(|tag| !is_nightly_tag(tag))
        || client_version
            .as_deref()
            .is_some_and(|version| parse_version(version).is_none())
    {
        return None;
    }
    Some(NoticeParts {
        stable_game_tag,
        nightly_game_tag,
        client_version,
    })
}

fn optional_string(value: &serde_json::Value, name: &str) -> Option<Option<String>> {
    match value.get(name)? {
        serde_json::Value::Null => Some(None),
        serde_json::Value::String(text) => Some(Some(text.clone())),
        _ => None,
    }
}

fn is_stable_tag(tag: &str) -> bool {
    tag.strip_prefix('v')
        .is_some_and(|version| parse_version(version).is_some())
}

fn save_cache(path: &Path, installed: &InstalledRelease, notice: &NoticeParts, checked_at: u64) {
    let value = serde_json::json!({
        "schema_version": 1,
        "checked_at": checked_at,
        "patch_version": installed.patch_version,
        "client_version": installed.client_version,
        "upstream_commit": installed.upstream_commit,
        "notice": {
            "stable_game_tag": notice.stable_game_tag,
            "nightly_game_tag": notice.nightly_game_tag,
            "client_version": notice.client_version,
        },
    });
    let Ok(bytes) = serde_json::to_vec(&value) else {
        return;
    };
    if let Some(parent) = path.parent() {
        if std::fs::create_dir_all(parent).is_err() {
            return;
        }
    }
    let temporary = path.with_extension("json.tmp");
    if std::fs::write(&temporary, &bytes).is_err() {
        return;
    }
    // Windows rename does not replace an existing file. Removing the old
    // non-sensitive cache first leaves only a short interval without a cache.
    let _ = std::fs::remove_file(path);
    let _ = std::fs::rename(temporary, path);
}

#[cfg(test)]
mod tests {
    use super::*;

    const REAL_CATALOG: &[u8] = include_bytes!("../tests/fixtures/catalog-v1.json");
    const REAL_SIGNATURE: &[u8] = include_bytes!("../tests/fixtures/catalog-v1.sig");

    #[test]
    fn appx_metadata_is_strict_and_uses_the_catalog_identity() {
        let bytes = br#"{"schema_version":1,"patch_version":"0.1.0","client_version":"0.1.1","upstream_commit":"F42EF42EF6CECCB3C6A20BEA2481D87949A51C81"}"#;
        let installed = InstalledRelease::parse_json(bytes).unwrap();
        assert_eq!(installed.client_version, "0.1.1");
        assert_eq!(
            installed.upstream_commit,
            "f42ef42ef6ceccb3c6a20bea2481d87949a51c81"
        );
        assert!(InstalledRelease::parse_json(br#"{"schema_version":1,"patch_version":"0.1","client_version":"0.1.1","upstream_commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}"#).is_none());
        assert!(InstalledRelease::parse_json(br#"{"schema_version":2,"patch_version":"0.1.0","client_version":"0.1.1","upstream_commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}"#).is_none());
    }

    #[test]
    fn bundled_catalog_key_accepts_signed_catalog_and_rejects_tampering() {
        let versions = verify_and_parse_catalog(REAL_CATALOG, REAL_SIGNATURE).unwrap();
        assert_eq!(
            versions
                .into_iter()
                .max_by(|left, right| compare_versions(left, right))
                .as_deref(),
            Some("0.1.1")
        );
        let mut tampered = REAL_CATALOG.to_vec();
        tampered[0] ^= 1;
        assert!(verify_and_parse_catalog(&tampered, REAL_SIGNATURE).is_err());
    }

    #[test]
    fn game_release_selection_ignores_launcher_tags_and_keeps_stable_and_nightly_separate() {
        let releases = format!(
            r#"[
          {{"draft":false,"tag_name":"launcher@0.2.99","target_commitish":"{}","published_at":"2026-11-06T10:00:00Z"}},
          {{"draft":false,"tag_name":"v0.2.96","target_commitish":"{}","published_at":"2026-11-04T10:00:00Z"}},
          {{"draft":false,"tag_name":"v0.2.97","target_commitish":"{}","published_at":"2026-11-05T10:00:00Z"}},
          {{"draft":false,"tag_name":"nightly-2026-11-05","target_commitish":"{}","published_at":"2026-11-05T11:00:00Z"}},
          {{"draft":false,"tag_name":"nightly-2026-11-04","target_commitish":"{}","published_at":"2026-11-04T11:00:00Z"}}
        ]"#,
            "a".repeat(40),
            "b".repeat(40),
            "c".repeat(40),
            "d".repeat(40),
            "e".repeat(40)
        );
        let (stable, nightly) = select_game_releases(releases.as_bytes()).unwrap();
        assert_eq!(stable.unwrap().tag, "v0.2.97");
        assert_eq!(nightly.unwrap().tag, "nightly-2026-11-05");
    }

    #[test]
    fn signed_catalog_and_public_github_metadata_report_each_newer_stream() {
        let installed = InstalledRelease {
            patch_version: "0.1.0".to_owned(),
            client_version: "0.1.0".to_owned(),
            upstream_commit: "a".repeat(40),
        };
        let releases = format!(
            r#"[
              {{"draft":false,"tag_name":"launcher@0.2.99","target_commitish":"{}","published_at":"2026-11-06T10:00:00Z"}},
              {{"draft":false,"tag_name":"v0.2.97","target_commitish":"{}","published_at":"2026-11-05T10:00:00Z"}},
              {{"draft":false,"tag_name":"nightly-2026-11-05","target_commitish":"{}","published_at":"2026-11-05T11:00:00Z"}}
            ]"#,
            "f".repeat(40),
            "b".repeat(40),
            "c".repeat(40)
        );
        let mut comparisons = 0;
        let result = perform_check(&installed, |url, _| {
            if url == CATALOG_URL {
                Some(REAL_CATALOG.to_vec())
            } else if url == SIGNATURE_URL {
                Some(REAL_SIGNATURE.to_vec())
            } else if url == HORIZON_RELEASES_URL {
                Some(releases.as_bytes().to_vec())
            } else if url.starts_with(&format!("{GITHUB_API_BASE}/compare/")) {
                comparisons += 1;
                Some(br#"{"status":"ahead","ahead_by":3}"#.to_vec())
            } else {
                None
            }
        });
        assert!(result.complete);
        let notice = result.parts;
        assert_eq!(comparisons, 2);
        assert_eq!(notice.stable_game_tag.as_deref(), Some("v0.2.97"));
        assert_eq!(
            notice.nightly_game_tag.as_deref(),
            Some("nightly-2026-11-05")
        );
        assert_eq!(notice.client_version.as_deref(), Some("0.1.1"));
        assert!(
            notice
                .notice()
                .unwrap()
                .message
                .contains(CLIENT_RELEASE_BASE)
        );
    }

    #[test]
    fn only_an_ahead_comparison_is_reported() {
        assert!(compare_is_ahead(br#"{"status":"ahead","ahead_by":1}"#).unwrap());
        assert!(!compare_is_ahead(br#"{"status":"identical","ahead_by":0}"#).unwrap());
        assert!(!compare_is_ahead(br#"{"status":"diverged","ahead_by":2}"#).unwrap());
        assert!(!compare_is_ahead(br#"{"status":"behind","ahead_by":0}"#).unwrap());
    }

    #[test]
    fn version_and_tag_parsing_excludes_invalid_and_launcher_streams() {
        assert!(compare_versions("0.10.0", "0.9.99").is_gt());
        assert_eq!(parse_version("01.2.3"), None);
        assert!(is_nightly_tag("nightly-2026-11-05"));
        assert!(!is_nightly_tag("launcher@0.2.99"));
        assert!(!is_stable_tag("v0.2.97-rc1"));
    }

    #[test]
    fn cached_notice_is_checked_against_a_fresh_signature_free_local_record() {
        let root = std::env::temp_dir().join(format!("huwp-update-cache-{}", std::process::id()));
        let path = root.join("update-check.json");
        let installed = InstalledRelease {
            patch_version: "0.1.0".to_owned(),
            client_version: "0.1.1".to_owned(),
            upstream_commit: "a".repeat(40),
        };
        let parts = NoticeParts {
            stable_game_tag: Some("v0.2.97".to_owned()),
            nightly_game_tag: None,
            client_version: None,
        };
        save_cache(&path, &installed, &parts, 1000);
        let loaded = load_cache(&path, &installed, 1000 + 60, CACHE_TTL).unwrap();
        assert_eq!(loaded.notice, parts);
        assert!(load_cache(&path, &installed, 1000 + CACHE_TTL.as_secs() + 1, CACHE_TTL).is_none());
        assert!(
            load_cache(
                &path,
                &InstalledRelease {
                    client_version: "0.1.0".to_owned(),
                    ..installed
                },
                1000 + 60,
                CACHE_TTL
            )
            .is_none()
        );
        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn a_fresh_check_cache_avoids_all_repeat_network_requests() {
        let root =
            std::env::temp_dir().join(format!("huwp-update-cache-flow-{}", std::process::id()));
        let path = root.join("update-check.json");
        let installed = InstalledRelease {
            patch_version: "0.1.0".to_owned(),
            client_version: "0.1.0".to_owned(),
            upstream_commit: "a".repeat(40),
        };
        let releases = format!(
            r#"[
              {{"draft":false,"tag_name":"v0.2.97","target_commitish":"{}","published_at":"2026-11-05T10:00:00Z"}},
              {{"draft":false,"tag_name":"nightly-2026-11-05","target_commitish":"{}","published_at":"2026-11-05T11:00:00Z"}}
            ]"#,
            "b".repeat(40),
            "c".repeat(40)
        );
        let first = check_with_cache(&installed, &path, |url, _| {
            if url == CATALOG_URL {
                Some(REAL_CATALOG.to_vec())
            } else if url == SIGNATURE_URL {
                Some(REAL_SIGNATURE.to_vec())
            } else if url == HORIZON_RELEASES_URL {
                Some(releases.as_bytes().to_vec())
            } else if url.starts_with(&format!("{GITHUB_API_BASE}/compare/")) {
                Some(br#"{"status":"ahead","ahead_by":1}"#.to_vec())
            } else {
                None
            }
        });
        assert!(first.is_some());
        let cached = check_with_cache(&installed, &path, |_, _| {
            panic!("fresh cache should avoid release-network requests")
        });
        assert_eq!(cached.unwrap().stable_game_tag.as_deref(), Some("v0.2.97"));
        let _ = std::fs::remove_dir_all(root);
    }
}
