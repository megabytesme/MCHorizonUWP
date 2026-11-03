//! Shared Horizon account protocol. Transport is supplied by each platform.
//! Tokens are kept out of Debug output and transport errors.
pub mod sign_in;
pub mod vault;

// Shared protocol progress points; the launcher supplies its own richer UI type.
pub mod session {
    #[derive(Clone, Copy, Debug, PartialEq, Eq)]
    pub enum Phase {
        Microsoft,
        Xbox,
        Minecraft,
        Ownership,
        Profile,
    }
}

use serde_json::{json, Value};
use std::time::{Duration, SystemTime, UNIX_EPOCH};
const SCOPE: &str = "XboxLive.signin offline_access";
pub const NOT_CONFIGURED: &str = "This build has no Microsoft sign-in (HORIZON_MSA_CLIENT_ID).";
const DOES_NOT_OWN: &str = "This account doesn't own Minecraft: Java Edition.";
const NO_PROFILE: &str =
    "This account has no Minecraft profile yet. Create one at minecraft.net, then sign in again.";
const OTHER_PROFILE: &str =
    "The Microsoft account now has another Minecraft profile. Sign in again.";
const SIGN_IN_AGAIN: &str = "Your Microsoft sign-in expired. Sign in again.";
const RENEW_BEFORE: u64 = 3600;

struct AccountRequest {
    method: String,
    url: String,
    authorization: Option<String>,
}
impl AccountRequest {
    fn set(mut self, name: &str, value: &str) -> Self {
        if name.eq_ignore_ascii_case("authorization") {
            self.authorization = Some(value.to_owned());
        }
        self
    }
}
fn request_to(method: &str, url: &str) -> AccountRequest {
    AccountRequest {
        method: method.to_owned(),
        url: url.to_owned(),
        authorization: None,
    }
}
#[cfg(any(target_vendor = "uwp", test))]
fn form_encode(fields: &[(&str, &str)]) -> String {
    fn encode(value: &str) -> String {
        let mut out = String::new();
        for b in value.bytes() {
            match b {
                b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'*' | b'-' | b'.' | b'_' => {
                    out.push(char::from(b))
                }
                b' ' => out.push('+'),
                _ => out.push_str(&format!("%{b:02X}")),
            }
        }
        out
    }
    fields
        .iter()
        .map(|(k, v)| format!("{}={}", encode(k), encode(v)))
        .collect::<Vec<_>>()
        .join("&")
}
#[cfg(target_vendor = "uwp")]
fn send(
    request: AccountRequest,
    body: Option<Body<'_>>,
    service: &str,
) -> Result<(u16, Value), String> {
    let (payload, content_type) = match body {
        Some(Body::Form(fields)) => (
            Some(form_encode(&fields)),
            Some("application/x-www-form-urlencoded"),
        ),
        Some(Body::Json(value)) => (
            Some(value.to_string()),
            Some("application/json; charset=utf-8"),
        ),
        None => (None, None),
    };
    if request.method != "GET" && request.method != "POST" {
        return Err(format!("Could not reach {service}."));
    }
    let response = horizon_uwp_https_bridge::request(
        &request.url,
        payload.as_deref().map(str::as_bytes),
        content_type,
        request.authorization.as_deref(),
        1024 * 1024,
    )
    .ok_or_else(|| format!("Could not reach {service}. Check your internet connection."))?;
    Ok((
        response.status as u16,
        serde_json::from_slice(&response.body).unwrap_or(Value::Null),
    ))
}
#[cfg(not(any(target_vendor = "uwp", test)))]
fn send(
    request: AccountRequest,
    body: Option<Body<'_>>,
    service: &str,
) -> Result<(u16, Value), String> {
    // This crate's desktop transport is deliberately unavailable; consuming the
    // request here keeps the same protocol types without logging their contents.
    let _ = (request.method, request.url);
    match body {
        Some(Body::Form(fields)) => drop(fields),
        Some(Body::Json(value)) => drop(value),
        None => {}
    }
    Err(format!(
        "Could not reach {service}. Check your internet connection."
    ))
}
include!("protocol.rs");
#[cfg(test)]
thread_local! { static MOCK_STEP: std::cell::Cell<u32> = const { std::cell::Cell::new(0) }; }
#[cfg(test)]
fn send(
    request: AccountRequest,
    body: Option<Body<'_>>,
    _service: &str,
) -> Result<(u16, Value), String> {
    let step = MOCK_STEP.with(|s| {
        let n = s.get();
        s.set(n + 1);
        n
    });
    let (status, value) = match step {
        0 => {
            assert_eq!(request.method, "POST");
            assert!(request.url.ends_with("/devicecode"));
            let Some(Body::Form(fields)) = body else {
                panic!("expected form");
            };
            assert!(fields.contains(&("client_id", "fake-client")));
            (
                200,
                json!({"user_code":"TEST-CODE","device_code":"synthetic-device-code","interval":1,"expires_in":900,"verification_uri":"https://www.microsoft.com/link"}),
            )
        }
        1 => {
            assert!(request.url.ends_with("/token"));
            let Some(Body::Form(fields)) = body else {
                panic!("expected form");
            };
            assert!(fields.contains(&("device_code", "synthetic-device-code")));
            (400, json!({"error":"authorization_pending"}))
        }
        2 => (
            200,
            json!({"access_token":"synthetic-ms-access","refresh_token":"synthetic-refresh"}),
        ),
        3 => {
            assert!(request.url.ends_with("/authenticate"));
            let Some(Body::Json(value)) = body else {
                panic!("expected JSON");
            };
            assert_eq!(value["Properties"]["RpsTicket"], "d=synthetic-ms-access");
            (200, json!({"Token":"synthetic-xbl"}))
        }
        4 => (
            200,
            json!({"Token":"synthetic-xsts","DisplayClaims":{"xui":[{"uhs":"user-hash","xid":"123"}]}}),
        ),
        5 => (
            200,
            json!({"access_token":"synthetic-minecraft","expires_in":86400}),
        ),
        6 => {
            assert_eq!(
                request.authorization.as_deref(),
                Some("Bearer synthetic-minecraft")
            );
            (200, json!({"items":[{"name":"game_minecraft"}]}))
        }
        7 => (
            200,
            json!({"id":"00000000000000000000000000000001","name":"Tester"}),
        ),
        100 => {
            assert!(request.url.ends_with("/token"));
            (
                200,
                json!({"access_token":"renewed-access","refresh_token":"rotated-refresh"}),
            )
        }
        101 => {
            assert!(request.url.ends_with("/authenticate"));
            (503, Value::Null)
        }
        _ => panic!("unexpected request"),
    };
    Ok((status, value))
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn device_code_to_owned_minecraft_session_uses_shared_protocol() {
        MOCK_STEP.with(|s| s.set(0));
        let ep = Endpoints::live();
        let code = device_code(&ep, "fake-client").unwrap();
        assert_eq!(code.user_code, "TEST-CODE");
        assert!(!format!("{code:?}").contains("synthetic-device-code"));
        assert!(matches!(poll(&ep, "fake-client", &code), Ok(Poll::Pending)));
        let tokens = match poll(&ep, "fake-client", &code).unwrap() {
            Poll::Done(t) => t,
            _ => panic!("expected tokens"),
        };
        assert!(!format!("{tokens:?}").contains("synthetic"));
        let (account, secrets) = finish_sign_in(&ep, tokens).unwrap();
        assert_eq!(account.name, "Tester");
        assert!(!format!("{secrets:?}").contains("synthetic"));
        let session: Value = serde_json::from_str(&secrets.session_json(&account)).unwrap();
        assert_eq!(session["name"], "Tester");
        assert_eq!(session["access_token"], "synthetic-minecraft");
        assert_eq!(session["user_type"], "msa");
        MOCK_STEP.with(|s| assert_eq!(s.get(), 8));
    }
    #[test]
    fn rotated_refresh_survives_a_later_xbox_failure() {
        MOCK_STEP.with(|s| s.set(100));
        let account = Account {
            name: "Tester".into(),
            uuid: "00000000000000000000000000000001".into(),
            skin: None,
            cape: None,
        };
        let secrets: Secrets = serde_json::from_value(json!({"refresh_token":"previous-refresh","minecraft_token":"old-token","minecraft_expires_at":0,"owns":true})).unwrap();
        let Check::NoAnswer(_, returned) =
            check(&Endpoints::live(), Some("fake-client"), &account, secrets)
        else {
            panic!("expected service failure")
        };
        assert_eq!(returned.refresh_token, "rotated-refresh");
        assert!(returned.owns);
    }
    #[test]
    fn form_encoding_handles_non_ascii_and_reserved_characters() {
        assert_eq!(form_encode(&[("name", "a b+c/é")]), "name=a+b%2Bc%2F%C3%A9");
    }
}
