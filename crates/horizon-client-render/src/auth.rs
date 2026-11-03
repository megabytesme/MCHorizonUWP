//! The Microsoft account the launcher signed in, handed over in a session
//! file (docs/microsoft-account.md), and the Minecraft services call
//! online-mode servers need: authlib's MinecraftServicesSessionService.joinServer
//! through the services discovery document. The access token stays in this
//! process's memory: it is never logged, printed or written anywhere.
use std::{
    path::Path,
    sync::{Arc, Mutex, OnceLock},
    time::{Duration, SystemTime},
};

/// MinecraftServicesEnvironment.PROD.
pub const DEFAULT_DISCOVERY: &str = "https://discovery.minecraftservices.com/minecraft/client";
/// A session file older than this is refused: it can't be replayed later.
const MAX_SESSION_FILE_AGE: Duration = Duration::from_secs(120);

/// A signed-in account (vanilla's User with type msa).
pub struct AuthSession {
    pub name: String,
    pub uuid: u128,
    pub xuid: Option<String>,
    access_token: String,
}
impl std::fmt::Debug for AuthSession {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("AuthSession")
            .field("name", &self.name)
            .field("uuid", &horizon_protocol::crypto::uuid_string(self.uuid))
            .field("access_token", &"<hidden>")
            .finish()
    }
}

/// A UUID with or without dashes.
pub fn parse_uuid(text: &str) -> Option<u128> {
    let hex: String = text.chars().filter(|c| *c != '-').collect();
    (hex.len() == 32)
        .then(|| u128::from_str_radix(&hex, 16).ok())
        .flatten()
}

impl AuthSession {
    /// For tests and harnesses.
    pub fn new(name: &str, uuid: u128, access_token: &str) -> Self {
        Self {
            name: name.to_owned(),
            uuid,
            xuid: None,
            access_token: access_token.to_owned(),
        }
    }
    /// The session file's JSON: {"name", "uuid", "access_token", "user_type": "msa", "xuid"}.
    pub fn parse(text: &str) -> Result<Self, String> {
        let v: serde_json::Value =
            serde_json::from_str(text).map_err(|_| "the session is not valid JSON")?;
        let field = |k: &str| v.get(k).and_then(|s| s.as_str());
        let name = field("name").ok_or("the session has no name")?;
        if name.is_empty() || name.chars().count() > 16 {
            return Err("the session's name is not a valid player name".into());
        }
        let uuid = field("uuid")
            .and_then(parse_uuid)
            .ok_or("the session has no valid uuid")?;
        let access_token = field("access_token")
            .filter(|t| !t.is_empty())
            .ok_or("the session has no access token")?;
        if field("user_type").is_some_and(|t| t != "msa") {
            return Err("the session is not a Microsoft account session".into());
        }
        Ok(Self {
            name: name.to_owned(),
            uuid,
            xuid: field("xuid").map(str::to_owned),
            access_token: access_token.to_owned(),
        })
    }
    /// Read the session file the launcher wrote and delete it at once. It
    /// must be fresh and, on Unix, readable by its owner only.
    pub fn load(path: &Path) -> Result<Self, String> {
        let meta = std::fs::metadata(path).map_err(|e| format!("{}: {e}", path.display()))?;
        let refuse = |why: &str| {
            let _ = std::fs::remove_file(path);
            Err(format!("{}: {why}", path.display()))
        };
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            if meta.permissions().mode() & 0o077 != 0 {
                return refuse("other users can read it");
            }
        }
        let age = meta
            .modified()
            .ok()
            .and_then(|m| SystemTime::now().duration_since(m).ok())
            .unwrap_or_default();
        if age > MAX_SESSION_FILE_AGE {
            return refuse("it is too old (start the game from the launcher)");
        }
        let text = std::fs::read_to_string(path);
        let _ = std::fs::remove_file(path);
        Self::parse(&text.map_err(|e| format!("{}: {e}", path.display()))?)
    }
}

static CURRENT: OnceLock<Arc<AuthSession>> = OnceLock::new();

/// The account this game was started with (set once at start).
pub fn set_current(session: AuthSession) {
    let _ = try_set_current(session);
}

/// Install an account once, refusing to silently retain a previous account.
pub fn try_set_current(session: AuthSession) -> Result<(), &'static str> {
    install_session(&CURRENT, session)
}

fn install_session(
    slot: &OnceLock<Arc<AuthSession>>,
    session: AuthSession,
) -> Result<(), &'static str> {
    slot.set(Arc::new(session))
        .map_err(|_| "an account is already active in this process")
}
pub fn current() -> Option<Arc<AuthSession>> {
    CURRENT.get().cloned()
}

/// Why joining a server through the session service failed, like the
/// AuthenticationException subclasses authlib throws.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum JoinError {
    /// No answer, or a 5xx (AuthenticationUnavailableException).
    Unavailable,
    /// 401 or ForbiddenOperationException (InvalidCredentialsException).
    InvalidSession,
    /// InsufficientPrivilegesException: multiplayer is off for the account.
    InsufficientPrivileges,
    /// multiplayer.access.banned or FORCED_USERNAME_CHANGE.
    Banned,
    /// Anything else, with the service's message.
    Other(String),
}
impl JoinError {
    /// MinecraftClientHttpException.toAuthenticationException.
    pub fn from_response(status: u16, body: &str) -> Self {
        let v: serde_json::Value = serde_json::from_str(body).unwrap_or_default();
        let error = v.get("error").and_then(|e| e.as_str()).unwrap_or("");
        match error {
            "ForbiddenOperationException" => return Self::InvalidSession,
            "multiplayer.access.banned" | "FORCED_USERNAME_CHANGE" => return Self::Banned,
            "InsufficientPrivilegesException" => return Self::InsufficientPrivileges,
            _ => {}
        }
        match status {
            401 => Self::InvalidSession,
            500.. => Self::Unavailable,
            _ => Self::Other(
                v.get("errorMessage")
                    .and_then(|m| m.as_str())
                    .map_or_else(|| format!("Status: {status}"), str::to_owned),
            ),
        }
    }
    /// ClientHandshakePacketListenerImpl.authenticateServer's message: the
    /// key of the reason inside "disconnect.loginFailedInfo", or its text.
    pub fn reason(&self) -> Result<&'static str, &str> {
        match self {
            Self::Unavailable => Ok("disconnect.loginFailedInfo.serversUnavailable"),
            Self::InvalidSession => Ok("disconnect.loginFailedInfo.invalidSession"),
            Self::InsufficientPrivileges => Ok("disconnect.loginFailedInfo.insufficientPrivileges"),
            Self::Banned => Ok("disconnect.loginFailedInfo.userBanned"),
            Self::Other(message) => Err(message),
        }
    }
}

/// The services discovery document URL: `HORIZON_API_DISCOVERY` overrides
/// it like authlib's `-Dminecraft.api.discovery.host`.
pub fn discovery_url() -> String {
    std::env::var("HORIZON_API_DISCOVERY")
        .ok()
        .filter(|u| !u.is_empty())
        .unwrap_or_else(|| DEFAULT_DISCOVERY.to_owned())
}

#[cfg(not(all(feature = "uwp-https", target_vendor = "uwp")))]
fn agent() -> ureq::Agent {
    ureq::AgentBuilder::new()
        .timeout(Duration::from_secs(15))
        .user_agent(&format!("Horizon/{}", horizon_core::release::VERSION))
        .build()
}

/// The discovery document, fetched once per game (a failure is retried next time).
#[cfg(not(all(feature = "uwp-https", target_vendor = "uwp")))]
pub(crate) fn discovery() -> Result<Arc<serde_json::Value>, JoinError> {
    static CACHE: Mutex<Option<Arc<serde_json::Value>>> = Mutex::new(None);
    if let Some(d) = CACHE.lock().unwrap().clone() {
        return Ok(d);
    }
    let doc: serde_json::Value = agent()
        .get(&discovery_url())
        .call()
        .ok()
        .and_then(|r| r.into_string().ok())
        .and_then(|t| serde_json::from_str(&t).ok())
        .ok_or(JoinError::Unavailable)?;
    let doc = Arc::new(doc);
    *CACHE.lock().unwrap() = Some(doc.clone());
    Ok(doc)
}

#[cfg(all(feature = "uwp-https", target_vendor = "uwp"))]
fn uwp_http(
    url: &str,
    body: Option<&str>,
    authorization: Option<&str>,
) -> Result<(u16, String), JoinError> {
    let response = horizon_uwp_https_bridge::request(
        url,
        body.map(str::as_bytes),
        body.map(|_| "application/json; charset=utf-8"),
        authorization,
        1024 * 1024,
    )
    .ok_or(JoinError::Unavailable)?;
    Ok((
        response.status as u16,
        String::from_utf8_lossy(&response.body).into_owned(),
    ))
}

#[cfg(all(feature = "uwp-https", target_vendor = "uwp"))]
pub(crate) fn discovery() -> Result<Arc<serde_json::Value>, JoinError> {
    static CACHE: Mutex<Option<Arc<serde_json::Value>>> = Mutex::new(None);
    if let Some(d) = CACHE.lock().unwrap().clone() {
        return Ok(d);
    }
    let (status, body) = uwp_http(&discovery_url(), None, None)?;
    if !(200..300).contains(&status) {
        return Err(JoinError::Unavailable);
    }
    let doc = Arc::new(
        serde_json::from_str::<serde_json::Value>(&body).map_err(|_| JoinError::Unavailable)?,
    );
    *CACHE.lock().unwrap() = Some(doc.clone());
    Ok(doc)
}

/// MinecraftServicesDiscoveryService.getUrl(service, endpoint).
pub fn endpoint(doc: &serde_json::Value, service: &str, name: &str) -> Option<String> {
    doc.pointer(&format!("/discovery/{service}/endpoints/{name}/uri"))
        .and_then(|u| u.as_str())
        .map(str::to_owned)
}

/// `Minecraft`'s constructor futures for the title screen: `UserApiService.fetchProperties`
/// (the `player` service's `getAttributes`, with the bearer token) and
/// `SessionService.fetchProfile(uuid, true)` (`getProfileById`). A failed
/// attributes call falls back to the offline properties, a failed profile call
/// to no actions, as vanilla does. Blocks: call it off the main thread.
#[cfg(not(all(feature = "uwp-https", target_vendor = "uwp")))]
pub fn fetch_account_status(session: &AuthSession) -> crate::ban_notice::AccountStatus {
    use crate::ban_notice::{AccountStatus, parse_profile_actions, parse_properties};
    let Ok(doc) = discovery() else {
        return AccountStatus::default();
    };
    let properties = endpoint(&doc, "player", "getAttributes")
        .and_then(|url| {
            agent()
                .get(&url)
                .set("Authorization", &format!("Bearer {}", session.access_token))
                .call()
                .ok()?
                .into_string()
                .ok()
        })
        .and_then(|body| parse_properties(&body))
        .unwrap_or_default();
    let actions = endpoint(&doc, "session", "getProfileById")
        .and_then(|url| {
            let url = url.replace("{profileId}", &format!("{:032x}", session.uuid));
            agent()
                .get(&format!("{url}?unsigned=true"))
                .call()
                .ok()?
                .into_string()
                .ok()
        })
        .map(|body| parse_profile_actions(&body))
        .unwrap_or_default();
    AccountStatus {
        properties,
        actions,
    }
}

#[cfg(all(feature = "uwp-https", target_vendor = "uwp"))]
pub fn fetch_account_status(session: &AuthSession) -> crate::ban_notice::AccountStatus {
    use crate::ban_notice::{AccountStatus, parse_profile_actions, parse_properties};
    let Ok(doc) = discovery() else {
        return AccountStatus::default();
    };
    let properties = endpoint(&doc, "player", "getAttributes")
        .and_then(|url| {
            uwp_http(
                &url,
                None,
                Some(&format!("Bearer {}", session.access_token)),
            )
            .ok()
        })
        .filter(|(status, _)| (200..300).contains(status))
        .and_then(|(_, body)| parse_properties(&body))
        .unwrap_or_default();
    let actions = endpoint(&doc, "session", "getProfileById")
        .map(|url| url.replace("{profileId}", &format!("{:032x}", session.uuid)))
        .and_then(|url| uwp_http(&format!("{url}?unsigned=true"), None, None).ok())
        .filter(|(status, _)| (200..300).contains(status))
        .map(|(_, body)| parse_profile_actions(&body))
        .unwrap_or_default();
    AccountStatus {
        properties,
        actions,
    }
}

/// The JoinMinecraftServerRequest body; `selectedProfile` is the undashed UUID.
pub fn join_request(session: &AuthSession, server_hash: &str) -> String {
    serde_json::json!({
        "accessToken": session.access_token,
        "selectedProfile": format!("{:032x}", session.uuid),
        "serverId": server_hash,
    })
    .to_string()
}

/// MinecraftServicesSessionService.joinServer: tell the session service this
/// account is joining the server whose hash is `server_hash`.
#[cfg(not(all(feature = "uwp-https", target_vendor = "uwp")))]
pub fn join_server(session: &AuthSession, server_hash: &str) -> Result<(), JoinError> {
    let doc = discovery()?;
    let url = endpoint(&doc, "session", "join").ok_or(JoinError::Unavailable)?;
    match agent()
        .post(&url)
        .set("Content-Type", "application/json; charset=utf-8")
        .send_string(&join_request(session, server_hash))
    {
        Ok(_) => Ok(()),
        Err(ureq::Error::Status(status, response)) => Err(JoinError::from_response(
            status,
            &response.into_string().unwrap_or_default(),
        )),
        Err(ureq::Error::Transport(_)) => Err(JoinError::Unavailable),
    }
}

#[cfg(all(feature = "uwp-https", target_vendor = "uwp"))]
pub fn join_server(session: &AuthSession, server_hash: &str) -> Result<(), JoinError> {
    let doc = discovery()?;
    let url = endpoint(&doc, "session", "join").ok_or(JoinError::Unavailable)?;
    let (status, body) = uwp_http(&url, Some(&join_request(session, server_hash)), None)?;
    uwp_join_result(status, &body)
}

/// What the session service says about a player joining this server
/// (MinecraftServicesSessionService.hasJoinedServer).
#[derive(Clone, Debug, PartialEq)]
pub struct JoinedProfile {
    pub uuid: u128,
    pub properties: Vec<horizon_protocol::packets::login::Property>,
}

/// HttpDiscoveryService.buildQuery: `URLEncoder.encode(value, UTF-8)`.
fn url_encode(text: &str) -> String {
    let mut out = String::new();
    for b in text.bytes() {
        match b {
            b'a'..=b'z' | b'A'..=b'Z' | b'0'..=b'9' | b'.' | b'-' | b'*' | b'_' => {
                out.push(char::from(b));
            }
            b' ' => out.push('+'),
            _ => out.push_str(&format!("%{b:02X}")),
        }
    }
    out
}

/// The session service's verify endpoint, asked by an online-mode server
/// whether `name` joined with `server_hash` (and from `ip`, when the server
/// prevents proxy connections). `Ok(None)`: it didn't, or the service said
/// no; `Err(Unavailable)`: no answer.
#[cfg(not(all(feature = "uwp-https", target_vendor = "uwp")))]
pub fn has_joined(
    name: &str,
    server_hash: &str,
    ip: Option<std::net::IpAddr>,
) -> Result<Option<JoinedProfile>, JoinError> {
    let doc = discovery()?;
    let url = endpoint(&doc, "session", "verify").ok_or(JoinError::Unavailable)?;
    let mut query = format!(
        "username={}&serverId={}",
        url_encode(name),
        url_encode(server_hash)
    );
    if let Some(ip) = ip {
        query.push_str(&format!("&ip={}", url_encode(&ip.to_string())));
    }
    let separator = if url.contains('?') { '&' } else { '?' };
    let body = match agent().get(&format!("{url}{separator}{query}")).call() {
        Ok(response) => response.into_string().unwrap_or_default(),
        Err(ureq::Error::Status(status, response)) => {
            let error =
                JoinError::from_response(status, &response.into_string().unwrap_or_default());
            return match error {
                JoinError::Unavailable => Err(error),
                _ => Ok(None),
            };
        }
        Err(ureq::Error::Transport(_)) => return Err(JoinError::Unavailable),
    };
    Ok(parse_joined(&body))
}

#[cfg(all(feature = "uwp-https", target_vendor = "uwp"))]
pub fn has_joined(
    name: &str,
    server_hash: &str,
    ip: Option<std::net::IpAddr>,
) -> Result<Option<JoinedProfile>, JoinError> {
    let doc = discovery()?;
    let url = endpoint(&doc, "session", "verify").ok_or(JoinError::Unavailable)?;
    let mut query = format!(
        "username={}&serverId={}",
        url_encode(name),
        url_encode(server_hash)
    );
    if let Some(ip) = ip {
        query.push_str(&format!("&ip={}", url_encode(&ip.to_string())));
    }
    let separator = if url.contains('?') { '&' } else { '?' };
    let (status, body) = uwp_http(&format!("{url}{separator}{query}"), None, None)?;
    uwp_verify_result(status, &body)
}

#[cfg(any(test, all(feature = "uwp-https", target_vendor = "uwp")))]
fn uwp_join_result(status: u16, body: &str) -> Result<(), JoinError> {
    if (200..300).contains(&status) {
        Ok(())
    } else if status < 400 {
        Err(JoinError::Unavailable)
    } else {
        Err(JoinError::from_response(status, body))
    }
}

#[cfg(any(test, all(feature = "uwp-https", target_vendor = "uwp")))]
fn uwp_verify_result(status: u16, body: &str) -> Result<Option<JoinedProfile>, JoinError> {
    if (200..300).contains(&status) {
        return Ok(parse_joined(body));
    }
    if status < 400 {
        return Err(JoinError::Unavailable);
    }
    match JoinError::from_response(status, body) {
        JoinError::Unavailable => Err(JoinError::Unavailable),
        _ => Ok(None),
    }
}

/// HasJoinedMinecraftServerResponse: `id` and `properties`; no id is no profile.
pub fn parse_joined(body: &str) -> Option<JoinedProfile> {
    let v: serde_json::Value = serde_json::from_str(body).ok()?;
    let uuid = parse_uuid(v.get("id")?.as_str()?)?;
    let properties = v
        .get("properties")
        .and_then(|p| p.as_array())
        .into_iter()
        .flatten()
        .filter_map(|p| {
            Some(horizon_protocol::packets::login::Property {
                name: p.get("name")?.as_str()?.to_owned(),
                value: p.get("value")?.as_str()?.to_owned(),
                signature: p
                    .get("signature")
                    .and_then(|s| s.as_str())
                    .map(str::to_owned),
            })
        })
        .collect();
    Some(JoinedProfile { uuid, properties })
}

#[cfg(test)]
mod tests {
    #[test]
    fn uwp_session_http_statuses() {
        use super::{JoinError, uwp_join_result, uwp_verify_result};
        assert_eq!(uwp_join_result(204, ""), Ok(()));
        assert_eq!(uwp_join_result(302, ""), Err(JoinError::Unavailable));
        assert_eq!(uwp_join_result(401, ""), Err(JoinError::InvalidSession));
        assert_eq!(uwp_join_result(503, ""), Err(JoinError::Unavailable));
        assert_eq!(uwp_verify_result(204, ""), Ok(None));
        assert_eq!(uwp_verify_result(401, ""), Ok(None));
        assert_eq!(uwp_verify_result(503, ""), Err(JoinError::Unavailable));
    }
    use super::*;

    const UUID: u128 = 0x069a79f444e94726a5befca90e38aaf5;

    #[test]
    fn installing_a_second_account_fails_without_exposing_tokens() {
        let slot = OnceLock::new();
        install_session(&slot, AuthSession::new("First", UUID, "first-secret")).unwrap();
        let error =
            install_session(&slot, AuthSession::new("Second", UUID, "second-secret")).unwrap_err();
        assert!(!error.contains("secret"));
        assert_eq!(slot.get().unwrap().name, "First");
        assert!(!format!("{:?}", slot.get().unwrap()).contains("first-secret"));
    }

    fn write(dir: &Path, text: &str, mode: u32) -> std::path::PathBuf {
        let path = dir.join("session.json");
        std::fs::write(&path, text).unwrap();
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(&path, std::fs::Permissions::from_mode(mode)).unwrap();
        }
        let _ = mode;
        path
    }

    #[test]
    fn verify_answers() {
        assert_eq!(url_encode("Tester_1"), "Tester_1");
        assert_eq!(url_encode("-5a f"), "-5a+f");
        assert_eq!(url_encode("::1"), "%3A%3A1");
        let body = r#"{"id":"069a79f444e94726a5befca90e38aaf5","name":"Notch","properties":[{"name":"textures","value":"e30=","signature":"c2ln"}]}"#;
        let p = parse_joined(body).unwrap();
        assert_eq!(p.uuid, UUID);
        assert_eq!(p.properties.len(), 1);
        assert_eq!(p.properties[0].signature.as_deref(), Some("c2ln"));
        assert_eq!(parse_joined(""), None);
        assert_eq!(parse_joined(r#"{"name":"Notch"}"#), None);
    }

    #[test]
    fn session_files_are_read_once_and_must_be_private() {
        let dir = std::env::temp_dir().join(format!("horizon-auth-test-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let text = r#"{"name":"Notch","uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","access_token":"mock-token","user_type":"msa"}"#;
        let path = write(&dir, text, 0o600);
        let s = AuthSession::load(&path).unwrap();
        assert_eq!((s.name.as_str(), s.uuid), ("Notch", UUID));
        assert!(!path.exists(), "deleted after reading");
        assert!(!format!("{s:?}").contains("mock-token"));
        #[cfg(unix)]
        {
            let path = write(&dir, text, 0o644);
            assert!(
                AuthSession::load(&path)
                    .unwrap_err()
                    .contains("other users")
            );
            assert!(!path.exists());
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn session_file_fields() {
        assert!(
            AuthSession::parse(r#"{"name":"Notch","uuid":"069a79f444e94726a5befca90e38aaf5"}"#)
                .is_err()
        );
        assert!(
            AuthSession::parse(
                r#"{"name":"","uuid":"069a79f444e94726a5befca90e38aaf5","access_token":"t"}"#
            )
            .is_err()
        );
        assert!(AuthSession::parse(r#"{"name":"A","uuid":"nope","access_token":"t"}"#).is_err());
        assert!(AuthSession::parse(r#"{"name":"A","uuid":"069a79f444e94726a5befca90e38aaf5","access_token":"t","user_type":"legacy"}"#).is_err());
        let s = AuthSession::parse(r#"{"name":"A","uuid":"069a79f444e94726a5befca90e38aaf5","access_token":"t","xuid":"25"}"#).unwrap();
        assert_eq!(s.xuid.as_deref(), Some("25"));
    }

    #[test]
    fn join_request_like_authlib() {
        let s = AuthSession::new("Notch", UUID, "tok");
        let v: serde_json::Value = serde_json::from_str(&join_request(&s, "-7c9d")).unwrap();
        assert_eq!(
            v,
            serde_json::json!({"accessToken": "tok", "selectedProfile": "069a79f444e94726a5befca90e38aaf5", "serverId": "-7c9d"})
        );
    }

    #[test]
    fn errors_map_like_authlib() {
        let e = |status, body| JoinError::from_response(status, body);
        assert_eq!(
            e(
                403,
                r#"{"error":"ForbiddenOperationException","errorMessage":"Invalid token"}"#
            ),
            JoinError::InvalidSession
        );
        assert_eq!(e(401, ""), JoinError::InvalidSession);
        assert_eq!(
            e(403, r#"{"error":"InsufficientPrivilegesException"}"#),
            JoinError::InsufficientPrivileges
        );
        assert_eq!(
            e(403, r#"{"error":"multiplayer.access.banned"}"#),
            JoinError::Banned
        );
        assert_eq!(e(503, "down"), JoinError::Unavailable);
        assert_eq!(
            e(429, r#"{"errorMessage":"Too many"}"#),
            JoinError::Other("Too many".into())
        );
        assert_eq!(
            JoinError::InvalidSession.reason(),
            Ok("disconnect.loginFailedInfo.invalidSession")
        );
    }

    #[test]
    fn discovery_endpoints() {
        let doc = serde_json::json!({"discovery": {"session": {"endpoints": {"join": {"uri": "https://s/join"}}}}});
        assert_eq!(
            endpoint(&doc, "session", "join").as_deref(),
            Some("https://s/join")
        );
        assert_eq!(endpoint(&doc, "session", "verify"), None);
    }
}
