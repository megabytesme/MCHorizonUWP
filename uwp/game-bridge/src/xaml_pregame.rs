//! Native UWP pre-game account flow. All tokens stay inside this process.
use horizon_account_core::{
    profiles::{self, Profile, ProfileStore},
    sign_in::{self, Attempt, Event},
    vault::{CredentialLocker, SecretStore},
};
use horizon_client::auth::AuthSession;
use std::{
    sync::{Arc, mpsc::TryRecvError},
    time::Duration,
};

unsafe extern "C" {
    fn horizon_uwp_dialog_show(text: *const u8, len: usize, kind: u32) -> i32;
    fn horizon_uwp_dialog_poll() -> i32;
    fn horizon_uwp_dialog_cancel();
    fn horizon_uwp_window_closed() -> i32;
    fn horizon_uwp_window_visible() -> i32;
    fn horizon_uwp_pump_events();
    fn horizon_uwp_page_tick();
    fn horizon_uwp_page_profile(text: *const u8, len: usize, kind: u32);
    fn horizon_uwp_page_skin(text: *const u8, len: usize);
    fn horizon_uwp_page_accounts(text: *const u8, len: usize);
    fn horizon_uwp_dialog_text(buffer: *mut u8, capacity: usize) -> usize;
}

const MENU: u32 = 0;
const ACCOUNT: u32 = 1;
const WAIT: u32 = 2;
const NOTICE: u32 = 3;
const CONFIRM: u32 = 4;
const SELECT_ACCOUNT: u32 = 5;
const OFFLINE_NAME: u32 = 7;
const ADD_ACCOUNT: u32 = 8;

pub struct Selection {
    pub session: Option<AuthSession>,
    pub offline_name: Option<String>,
}

fn closed() -> bool {
    unsafe { horizon_uwp_window_closed() != 0 }
}
fn pump() {
    unsafe { horizon_uwp_pump_events() };
    unsafe { horizon_uwp_page_tick() };
    std::thread::sleep(Duration::from_millis(20));
}
fn show(text: &str, kind: u32) -> bool {
    if unsafe { horizon_uwp_dialog_show(text.as_ptr(), text.len(), kind) == 0 } {
        true
    } else {
        crate::report_failure("Horizon could not display the startup page.");
        false
    }
}
fn poll() -> i32 {
    let result = unsafe { horizon_uwp_dialog_poll() };
    if result == -2 {
        crate::report_failure("The Horizon startup page stopped responding.");
        -1
    } else {
        result
    }
}
fn dismiss() -> bool {
    unsafe { horizon_uwp_dialog_cancel() };
    let deadline = std::time::Instant::now() + Duration::from_secs(5);
    while !closed() && poll() == 0 && std::time::Instant::now() < deadline {
        pump();
    }
    !closed() && poll() != 0
}
fn prompt(text: &str, kind: u32) -> i32 {
    if !show(text, kind) {
        return -1;
    }
    while !closed() {
        let selected = poll();
        if selected != 0 {
            return selected;
        }
        pump();
    }
    -1
}
fn notice(text: &str) {
    let _ = prompt(text, NOTICE);
}

enum Outcome {
    Play(AuthSession, Option<String>),
    SignedOut,
    Menu,
    Exit,
}

fn wait(attempt: Attempt) -> Outcome {
    if !show("Contacting the account service…", WAIT) {
        return Outcome::Exit;
    }
    loop {
        if closed() {
            attempt.cancel();
            return Outcome::Exit;
        }
        if poll() != 0 {
            attempt.cancel();
            return Outcome::Menu;
        }
        match attempt.try_recv() {
            Ok(Event::Prompt {
                user_code,
                verification_uri,
            }) => {
                if !dismiss() {
                    return Outcome::Exit;
                }
                if closed() {
                    attempt.cancel();
                    return Outcome::Exit;
                }
                let text = format!(
                    "On another device, open {verification_uri} and enter:\n\n{user_code}\n\nReturn here after approving sign-in."
                );
                if !show(&text, WAIT) {
                    attempt.cancel();
                    return Outcome::Exit;
                }
            }
            Ok(Event::Ready(session)) => {
                if !dismiss() {
                    return Outcome::Exit;
                }
                let skin = session
                    .account
                    .skin
                    .as_ref()
                    .and_then(|(url, _)| profiles::safe_skin_url(url));
                // AuthSession::parse returns fixed errors and never echoes the JSON.
                return match AuthSession::parse(&session.into_json()) {
                    Ok(session) => Outcome::Play(session, skin),
                    Err(_) => {
                        notice("The account session could not be opened.");
                        Outcome::Menu
                    }
                };
            }
            Ok(Event::Offline(_)) => {
                if !dismiss() {
                    return Outcome::Exit;
                }
                notice(
                    "The account service could not be reached. Start the game without signing in to play offline.",
                );
                return Outcome::SignedOut;
            }
            Ok(Event::NoAccount) => {
                if !dismiss() {
                    return Outcome::Exit;
                }
                notice("No saved account was found. Choose Sign in to add one.");
                return Outcome::SignedOut;
            }
            Ok(Event::SignedOut { removed }) => {
                if !dismiss() {
                    return Outcome::Exit;
                }
                notice(if removed {
                    "The saved account was removed."
                } else {
                    "No saved account was found."
                });
                return Outcome::SignedOut;
            }
            Ok(Event::Failed(error)) => {
                if !dismiss() {
                    return Outcome::Exit;
                }
                notice(&error.to_string());
                return Outcome::Menu;
            }
            Ok(Event::Cancelled) => {
                if !dismiss() {
                    return Outcome::Exit;
                }
                return Outcome::Menu;
            }
            Err(TryRecvError::Empty) => pump(),
            Err(TryRecvError::Disconnected) => {
                if !dismiss() {
                    return Outcome::Exit;
                }
                notice("The account operation ended unexpectedly.");
                return Outcome::Menu;
            }
        }
    }
}

fn set_profile(name: Option<&str>, kind: u32) {
    let bytes = name.unwrap_or("").as_bytes();
    unsafe { horizon_uwp_page_profile(bytes.as_ptr(), bytes.len(), kind) };
}

fn set_skin(url: Option<&str>) {
    let bytes = url.unwrap_or("").as_bytes();
    unsafe { horizon_uwp_page_skin(bytes.as_ptr(), bytes.len()) };
}

fn choose_profile(store: &dyn SecretStore, question: &str) -> Option<Profile> {
    let profiles = match profiles::list(store) {
        Ok(profiles) => profiles,
        Err(_) => {
            notice(
                "The saved accounts could not be opened. Your existing credentials were preserved.",
            );
            return None;
        }
    };
    if profiles.is_empty() {
        notice("No saved accounts were found. Add an online or offline account first.");
        return None;
    }
    let mut labels = String::new();
    for profile in &profiles {
        let name: String = profile
            .name
            .chars()
            .map(|c| {
                if c == '\n' || c == '\r' || c == '\t' {
                    ' '
                } else {
                    c
                }
            })
            .collect();
        labels.push_str(if profile.online {
            "Online\t"
        } else {
            "Offline\t"
        });
        labels.push_str(&name);
        labels.push('\n');
    }
    unsafe { horizon_uwp_page_accounts(labels.as_ptr(), labels.len()) };
    let answer = prompt(question, SELECT_ACCOUNT);
    answer
        .checked_sub(100)
        .and_then(|i| profiles.get(i as usize))
        .cloned()
}

/// None means the window was closed. A selection without a session is offline.
pub fn start() -> Option<Selection> {
    let deadline = std::time::Instant::now() + Duration::from_secs(5);
    while !closed()
        && unsafe { horizon_uwp_window_visible() == 0 }
        && std::time::Instant::now() < deadline
    {
        pump();
    }
    if closed() {
        return None;
    }
    let store: Arc<dyn SecretStore> = Arc::new(CredentialLocker);
    let client_id = crate::account_client_id::client_id(option_env!("HORIZON_MSA_CLIENT_ID"));
    let mut active = Selection {
        session: None,
        offline_name: None,
    };
    let mut active_id: Option<String> = None;
    let mut pending_online: Option<String> = None;
    match profiles::selected(store.as_ref()) {
        Ok(Some(profile)) if profile.online => {
            set_profile(Some(&profile.name), 3);
            set_skin(profile.skin.as_deref());
            active_id = Some(profile.id.clone());
            pending_online = Some(profile.id);
        }
        Ok(Some(profile)) => {
            set_profile(Some(&profile.name), 2);
            set_skin(None);
            active_id = Some(profile.id);
            active.offline_name = Some(profile.name);
        }
        Ok(None) => {}
        Err(_) => {
            notice("Saved accounts could not be opened. Existing credentials were preserved.")
        }
    }
    loop {
        match prompt("Play Minecraft: Java Edition with Horizon.", MENU) {
            1 => {
                if let Some(id) = pending_online.as_ref() {
                    let selected = match ProfileStore::selected(Arc::clone(&store), id.clone()) {
                        Ok(selected) => selected,
                        Err(_) => {
                            notice("The saved account could not be opened.");
                            continue;
                        }
                    };
                    match wait(sign_in::restore(
                        client_id.map(str::to_owned),
                        Arc::new(selected),
                    )) {
                        Outcome::Play(session, _) => {
                            return Some(Selection {
                                session: Some(session),
                                offline_name: None,
                            });
                        }
                        Outcome::Exit => return None,
                        _ => continue,
                    }
                }
                return Some(active);
            }
            2 => {
                let account_action = match prompt("Manage the accounts on this device.", ACCOUNT) {
                    1 => match prompt("Choose how to add an account.", ADD_ACCOUNT) {
                        1 => 1,
                        2 => 3,
                        _ => 0,
                    },
                    2 => 2,
                    3 => 4,
                    _ => 0,
                };
                let outcome = match account_action {
                    1 => match client_id {
                        Some(id) => {
                            let account_store: Arc<dyn SecretStore> =
                                Arc::new(ProfileStore::adding(Arc::clone(&store)));
                            match sign_in::start(id.to_owned(), account_store) {
                                Ok(attempt) => wait(attempt),
                                Err(error) => {
                                    notice(&error.to_string());
                                    Outcome::Menu
                                }
                            }
                        }
                        None => {
                            notice(
                                "Microsoft sign-in is not configured for this build.",
                            );
                            Outcome::Menu
                        }
                    },
                    2 => match choose_profile(store.as_ref(), "Choose an account to use.") {
                        Some(profile) if profile.online => {
                            match ProfileStore::selected(Arc::clone(&store), profile.id) {
                                Ok(selected) => wait(sign_in::restore(
                                    client_id.map(str::to_owned),
                                    Arc::new(selected),
                                )),
                                Err(_) => {
                                    notice("The selected account could not be opened.");
                                    Outcome::Menu
                                }
                            }
                        }
                        Some(profile) => {
                            if profiles::select(store.as_ref(), &profile.id).is_err() {
                                notice("The account selection could not be saved for next time.");
                            }
                            set_profile(Some(&profile.name), 2);
                            set_skin(None);
                            active_id = Some(profile.id);
                            pending_online = None;
                            active = Selection {
                                session: None,
                                offline_name: Some(profile.name),
                            };
                            Outcome::Menu
                        }
                        None => Outcome::Menu,
                    },
                    3 => {
                        if prompt(
                            "Choose an offline player name (3–16 letters, digits or underscores).",
                            OFFLINE_NAME,
                        ) == 1
                        {
                            let mut buffer = [0u8; 64];
                            let length = unsafe {
                                horizon_uwp_dialog_text(buffer.as_mut_ptr(), buffer.len())
                            };
                            let name = buffer
                                .get(..length)
                                .and_then(|b| std::str::from_utf8(b).ok())
                                .unwrap_or("");
                            match profiles::add_offline(store.as_ref(), name) {
                                Ok(profile) => {
                                    if profiles::select(store.as_ref(), &profile.id).is_err() {
                                        notice(
                                            "The account selection could not be saved for next time.",
                                        );
                                    }
                                    set_profile(Some(&profile.name), 2);
                                    set_skin(None);
                                    active_id = Some(profile.id);
                                    pending_online = None;
                                    active = Selection {
                                        session: None,
                                        offline_name: Some(profile.name),
                                    };
                                }
                                Err(_) => notice(
                                    "The offline account could not be added. Check the name and available account slots.",
                                ),
                            }
                        }
                        Outcome::Menu
                    }
                    4 => {
                        if let Some(profile) =
                            choose_profile(store.as_ref(), "Choose an account to remove.")
                        {
                            if prompt("Remove this account from this app?", CONFIRM) == 1 {
                                match profiles::remove(store.as_ref(), &profile.id) {
                                    Ok(true) => {
                                        if active_id.as_deref() == Some(profile.id.as_str()) {
                                            active_id = None;
                                            pending_online = None;
                                            active = Selection {
                                                session: None,
                                                offline_name: None,
                                            };
                                            set_profile(None, 0);
                                            set_skin(None);
                                        }
                                        notice("The account was removed.");
                                    }
                                    Ok(false) => notice("The account was not found."),
                                    Err(_) => notice("The account could not be removed."),
                                }
                            }
                        }
                        Outcome::Menu
                    }
                    _ => Outcome::Menu,
                };
                match outcome {
                    Outcome::Play(session, skin) => {
                        let id = format!("online:{:032x}", session.uuid);
                        if profiles::select(store.as_ref(), &id).is_err() {
                            notice("The account selection could not be saved for next time.");
                        }
                        active_id = Some(id);
                        pending_online = None;
                        set_profile(Some(&session.name), 1);
                        set_skin(skin.as_deref());
                        active = Selection {
                            session: Some(session),
                            offline_name: None,
                        };
                    }
                    Outcome::SignedOut => {
                        active_id = None;
                        pending_online = None;
                        active = Selection {
                            session: None,
                            offline_name: None,
                        };
                        set_profile(None, 0);
                        set_skin(None);
                    }
                    Outcome::Exit => return None,
                    Outcome::Menu => {}
                }
            }
            _ => return None,
        }
    }
}
