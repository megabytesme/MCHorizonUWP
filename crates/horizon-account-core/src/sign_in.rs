//! Cancellable, background device-code sign-in with secure storage.
use crate::{
    check, device_code, finish_sign_in, vault, wait_for_sign_in, Account, Check, Endpoints,
};
use std::fmt;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    mpsc, Arc, Mutex,
};

// Only one operation may refresh or replace the active account at a time.
// This prevents overlapping workers from losing a rotated refresh token.
static ACCOUNT_OPERATION: Mutex<()> = Mutex::new(());

/// A game session whose token is never included in debug output.
pub struct Session {
    pub account: Account,
    session_json: String,
}
impl Session {
    /// Consume the session for an in-memory handoff to the game; do not log it.
    pub fn into_json(self) -> String {
        self.session_json
    }
}
impl fmt::Debug for Session {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("Session")
            .field("account", &self.account)
            .field("session_json", &"<hidden>")
            .finish()
    }
}

#[derive(Debug, PartialEq, Eq)]
pub enum SignInError {
    NotConfigured,
    RegistrationRejected,
    Declined,
    CodeExpired,
    DoesNotOwn,
    NoProfile,
    XboxSetup,
    RateLimited,
    Protocol,
    SecureStore,
}

impl fmt::Display for SignInError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::NotConfigured => "Microsoft sign-in is not configured for this build.",
            Self::RegistrationRejected => "Microsoft refused this app registration.",
            Self::Declined => "Sign-in was declined in the browser.",
            Self::CodeExpired => "The sign-in code expired. Start again.",
            Self::DoesNotOwn => "This account does not own Minecraft: Java Edition.",
            Self::NoProfile => "Create a Minecraft profile at minecraft.net, then sign in again.",
            Self::XboxSetup => "Check this account's Xbox profile and family settings at xbox.com.",
            Self::RateLimited => "Too many sign-ins. Wait a minute and try again.",
            Self::Protocol => "Sign-in failed. Check your connection and try again.",
            Self::SecureStore => "Could not save the account in Credential Locker.",
        })
    }
}

// Only fixed, known messages are surfaced. Unexpected server-provided text may
// contain secrets, so it is reduced to a generic error rather than displayed.
fn classify(message: &str) -> SignInError {
    match message {
        "Microsoft refused this launcher's app registration (client ID)." => SignInError::RegistrationRejected,
        "Sign-in was declined in the browser." => SignInError::Declined,
        "The sign-in code expired. Start again." => SignInError::CodeExpired,
        "This account doesn't own Minecraft: Java Edition." => SignInError::DoesNotOwn,
        "This account has no Minecraft profile yet. Create one at minecraft.net, then sign in again." => SignInError::NoProfile,
        "This Microsoft account has no Xbox profile yet. Sign in once at xbox.com to create one, then try again."
        | "Xbox Live is not available in your country or region."
        | "This account needs adult verification on xbox.com before it can play."
        | "This is a child account: an adult has to add it to a Microsoft family first." => SignInError::XboxSetup,
        "Too many sign-ins in a short time. Wait a minute and try again." => SignInError::RateLimited,
        _ => SignInError::Protocol,
    }
}

#[derive(Debug)]
pub enum Event {
    /// The public code and verification address for the player to enter.
    Prompt {
        user_code: String,
        verification_uri: String,
    },
    Ready(Session),
    /// The previous owner can play offline, but no online session was issued.
    Offline(Account),
    NoAccount,
    SignedOut {
        removed: bool,
    },
    Failed(SignInError),
    Cancelled,
}

/// Dropping an attempt asks its worker to stop; requests already in flight can
/// take until the HTTPS adapter's timeout to return.
pub struct Attempt {
    events: mpsc::Receiver<Event>,
    cancelled: Arc<AtomicBool>,
}
impl Attempt {
    pub fn try_recv(&self) -> Result<Event, mpsc::TryRecvError> {
        self.events.try_recv()
    }
    pub fn cancel(&self) {
        self.cancelled.store(true, Ordering::Release);
    }
}
impl Drop for Attempt {
    fn drop(&mut self) {
        self.cancel();
    }
}

/// Starts a sign-in on a worker. The complete account is saved in the provided
/// secure store before a session is handed to the caller. There is no disk or
/// memory-only fallback if that write fails.
pub fn start(
    client_id: String,
    store: Arc<dyn vault::SecretStore>,
) -> Result<Attempt, SignInError> {
    let client_id = client_id.trim().to_owned();
    if client_id.is_empty() {
        return Err(SignInError::NotConfigured);
    }
    let (send, events) = mpsc::channel();
    let cancelled = Arc::new(AtomicBool::new(false));
    let worker_cancelled = Arc::clone(&cancelled);
    std::thread::spawn(move || {
        let stopped = || worker_cancelled.load(Ordering::Acquire);
        let _guard = match ACCOUNT_OPERATION.lock() {
            Ok(guard) => guard,
            Err(_) => {
                let _ = send.send(Event::Failed(SignInError::Protocol));
                return;
            }
        };
        if stopped() {
            let _ = send.send(Event::Cancelled);
            return;
        }
        let outcome = (|| {
            let endpoints = Endpoints::live();
            let code = device_code(&endpoints, &client_id).map_err(|error| classify(&error))?;
            if stopped() {
                return Ok(None);
            }
            if send
                .send(Event::Prompt {
                    user_code: code.user_code.clone(),
                    verification_uri: code.verification_uri.clone(),
                })
                .is_err()
            {
                return Ok(None);
            }
            let tokens = match wait_for_sign_in(&endpoints, &client_id, &code, &stopped) {
                Ok(tokens) => tokens,
                Err(_) if stopped() => return Ok(None),
                Err(error) => return Err(classify(&error)),
            };
            if stopped() {
                return Ok(None);
            }
            let (account, secrets) =
                finish_sign_in(&endpoints, tokens).map_err(|error| classify(&error))?;
            if stopped() {
                return Ok(None);
            }
            let stored = vault::StoredAccount::new(account, secrets);
            vault::save(store.as_ref(), &stored).map_err(|_| SignInError::SecureStore)?;
            let session_json = stored.secrets.session_json(&stored.account);
            Ok(Some(Session {
                account: stored.account,
                session_json,
            }))
        })();
        let event = match outcome {
            Ok(Some(session)) => Event::Ready(session),
            Ok(None) => Event::Cancelled,
            Err(error) => Event::Failed(error),
        };
        let _ = send.send(event);
    });
    Ok(Attempt { events, cancelled })
}

/// Restores and rechecks the active account on a worker. A network failure
/// permits an established owner to play offline without handing out a session.
pub fn restore(client_id: Option<String>, store: Arc<dyn vault::SecretStore>) -> Attempt {
    let (send, events) = mpsc::channel();
    let cancelled = Arc::new(AtomicBool::new(false));
    let worker_cancelled = Arc::clone(&cancelled);
    std::thread::spawn(move || {
        let stopped = || worker_cancelled.load(Ordering::Acquire);
        let _guard = match ACCOUNT_OPERATION.lock() {
            Ok(guard) => guard,
            Err(_) => {
                let _ = send.send(Event::Failed(SignInError::Protocol));
                return;
            }
        };
        if stopped() {
            let _ = send.send(Event::Cancelled);
            return;
        }
        let event = match vault::load(store.as_ref()) {
            _ if stopped() => Event::Cancelled,
            Err(_) => Event::Failed(SignInError::SecureStore),
            Ok(None) => Event::NoAccount,
            Ok(Some(stored)) => {
                let account = stored.account;
                let answer = check(
                    &Endpoints::live(),
                    client_id.as_deref(),
                    &account,
                    stored.secrets,
                );
                // A refresh may have rotated the stored token. Persist its
                // result even if cancellation arrived during the request.
                match finish_restore(store.as_ref(), account, answer) {
                    Err(error) => Event::Failed(error),
                    Ok(_) if stopped() => Event::Cancelled,
                    Ok(event) => event,
                }
            }
        };
        let _ = send.send(event);
    });
    Attempt { events, cancelled }
}

/// Removes the active account on a worker, serialized with sign-in and refresh.
pub fn sign_out(store: Arc<dyn vault::SecretStore>) -> Attempt {
    let (send, events) = mpsc::channel();
    let cancelled = Arc::new(AtomicBool::new(false));
    let worker_cancelled = Arc::clone(&cancelled);
    std::thread::spawn(move || {
        let _guard = match ACCOUNT_OPERATION.lock() {
            Ok(guard) => guard,
            Err(_) => {
                let _ = send.send(Event::Failed(SignInError::Protocol));
                return;
            }
        };
        let event = if worker_cancelled.load(Ordering::Acquire) {
            Event::Cancelled
        } else {
            match vault::remove(store.as_ref()) {
                Ok(removed) => Event::SignedOut { removed },
                Err(_) => Event::Failed(SignInError::SecureStore),
            }
        };
        let _ = send.send(event);
    });
    Attempt { events, cancelled }
}

fn finish_restore(
    store: &dyn vault::SecretStore,
    previous: Account,
    answer: Check,
) -> Result<Event, SignInError> {
    let (account, secrets, online) = match answer {
        Check::Owns(account, secrets) => (account, secrets, true),
        Check::DoesNot(secrets) => {
            vault::save(store, &vault::StoredAccount::new(previous, secrets))
                .map_err(|_| SignInError::SecureStore)?;
            return Err(SignInError::DoesNotOwn);
        }
        Check::NoAnswer(_, secrets) => (previous, secrets, false),
    };
    let stored = vault::StoredAccount::new(account, secrets);
    // A refresh token may have rotated even if a later ownership request failed.
    vault::save(store, &stored).map_err(|_| SignInError::SecureStore)?;
    if online {
        let session_json = stored.secrets.session_json(&stored.account);
        Ok(Event::Ready(Session {
            account: stored.account,
            session_json,
        }))
    } else if stored.secrets.owns {
        Ok(Event::Offline(stored.account))
    } else {
        Err(SignInError::Protocol)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{Duration, Instant};

    #[derive(Default)]
    struct Store {
        value: Mutex<Option<String>>,
        fail: bool,
    }
    impl vault::SecretStore for Store {
        fn get(&self, _key: &str) -> Result<Option<String>, vault::StoreError> {
            Ok(self.value.lock().unwrap().clone())
        }
        fn set(&self, _key: &str, value: &str) -> Result<(), vault::StoreError> {
            if self.fail {
                return Err(vault::StoreError);
            }
            *self.value.lock().unwrap() = Some(value.to_owned());
            Ok(())
        }
        fn remove(&self, _key: &str) -> Result<bool, vault::StoreError> {
            Ok(self.value.lock().unwrap().take().is_some())
        }
    }
    fn next(attempt: &Attempt) -> Event {
        let deadline = Instant::now() + Duration::from_secs(30);
        loop {
            match attempt.try_recv() {
                Ok(event) => return event,
                Err(mpsc::TryRecvError::Empty) if Instant::now() < deadline => {
                    std::thread::sleep(Duration::from_millis(10))
                }
                error => panic!("unexpected event state: {error:?}"),
            }
        }
    }
    fn saved() -> vault::StoredAccount {
        let account = Account {
            uuid: "00000000000000000000000000000001".into(),
            name: "Tester".into(),
            skin: None,
            cape: None,
        };
        let secrets = serde_json::from_value(serde_json::json!({"refresh_token":"synthetic-refresh","minecraft_token":"synthetic-token","minecraft_expires_at":123,"owns":true})).unwrap();
        vault::StoredAccount::new(account, secrets)
    }
    #[test]
    fn restore_only_hands_out_an_online_session_after_secure_save() {
        let store = Store::default();
        let record = saved();
        let account = record.account.clone();
        let event = finish_restore(
            &store,
            account.clone(),
            Check::Owns(account, record.secrets),
        )
        .unwrap();
        let Event::Ready(session) = event else {
            panic!("expected online session")
        };
        assert!(!format!("{session:?}").contains("synthetic"));
        assert!(vault::load(&store).unwrap().is_some());
        let failed = Store {
            fail: true,
            ..Default::default()
        };
        let record = saved();
        let account = record.account.clone();
        assert!(matches!(
            finish_restore(
                &failed,
                account.clone(),
                Check::Owns(account, record.secrets)
            ),
            Err(SignInError::SecureStore)
        ));
    }
    #[test]
    fn restore_returns_offline_without_leaking_a_session_and_remembers_denial() {
        let store = Store::default();
        let record = saved();
        let previous = record.account.clone();
        let event = finish_restore(
            &store,
            previous.clone(),
            Check::NoAnswer("synthetic-secret".into(), record.secrets),
        )
        .unwrap();
        assert!(matches!(event, Event::Offline(account) if account.name == "Tester"));
        let record = vault::load(&store).unwrap().unwrap();
        let mut json = serde_json::to_value(&record.secrets).unwrap();
        json["owns"] = serde_json::json!(false);
        let denied = serde_json::from_value(json).unwrap();
        assert!(matches!(
            finish_restore(&store, previous, Check::DoesNot(denied)),
            Err(SignInError::DoesNotOwn)
        ));
        let record = vault::load(&store).unwrap().unwrap();
        assert!(!record.secrets.owns);
    }
    #[test]
    fn unknown_server_errors_are_not_echoed() {
        let error = classify("synthetic-secret-from-server");
        assert_eq!(error, SignInError::Protocol);
        assert!(!error.to_string().contains("synthetic"));
        assert_eq!(
            classify("This account doesn't own Minecraft: Java Edition."),
            SignInError::DoesNotOwn
        );
    }
    #[test]
    fn signs_in_stores_account_and_redacts_session() {
        let store = Arc::new(Store::default());
        let attempt = start("fake-client".into(), store.clone()).unwrap();
        assert!(
            matches!(next(&attempt), Event::Prompt { user_code, .. } if user_code == "TEST-CODE")
        );
        let Event::Ready(session) = next(&attempt) else {
            panic!("expected session")
        };
        assert_eq!(session.account.name, "Tester");
        assert!(!format!("{session:?}").contains("synthetic"));
        assert!(session.into_json().contains("synthetic-minecraft"));
        assert!(vault::load(store.as_ref()).unwrap().is_some());
    }
    #[test]
    fn sign_out_removes_the_stored_account() {
        let store = Arc::new(Store::default());
        vault::save(store.as_ref(), &saved()).unwrap();
        let attempt = sign_out(store.clone());
        assert!(matches!(next(&attempt), Event::SignedOut { removed: true }));
        assert!(vault::load(store.as_ref()).unwrap().is_none());
    }
    #[test]
    fn concurrent_operations_are_serialized_and_a_waiting_one_can_cancel() {
        let store = Arc::new(Store::default());
        let first = start("fake-client".into(), store.clone()).unwrap();
        assert!(matches!(next(&first), Event::Prompt { .. }));
        let second = start("fake-client".into(), store.clone()).unwrap();
        second.cancel();
        assert!(matches!(next(&first), Event::Ready(_)));
        assert!(matches!(next(&second), Event::Cancelled));
        assert!(vault::load(store.as_ref()).unwrap().is_some());
    }
    #[test]
    fn cancel_stops_before_saving() {
        let store = Arc::new(Store::default());
        let attempt = start("fake-client".into(), store.clone()).unwrap();
        assert!(matches!(next(&attempt), Event::Prompt { .. }));
        attempt.cancel();
        assert!(matches!(next(&attempt), Event::Cancelled));
        assert!(store.value.lock().unwrap().is_none());
    }
    #[test]
    fn store_failure_never_returns_a_session() {
        let store = Arc::new(Store {
            fail: true,
            ..Default::default()
        });
        let attempt = start("fake-client".into(), store).unwrap();
        assert!(matches!(next(&attempt), Event::Prompt { .. }));
        assert!(matches!(
            next(&attempt),
            Event::Failed(SignInError::SecureStore)
        ));
    }
}
