//! Native UWP pre-game account flow. All tokens stay inside this process.
use horizon_account_core::{
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
}

const MENU: u32 = 0;
const ACCOUNT: u32 = 1;
const WAIT: u32 = 2;
const NOTICE: u32 = 3;
const CONFIRM: u32 = 4;
const SAVED_ACCOUNT: u32 = 5;
const REPLACE: u32 = 6;

fn closed() -> bool {
    unsafe { horizon_uwp_window_closed() != 0 }
}
fn pump() {
    unsafe { horizon_uwp_pump_events() };
    std::thread::sleep(Duration::from_millis(20));
}
fn show(text: &str, kind: u32) -> bool {
    unsafe { horizon_uwp_dialog_show(text.as_ptr(), text.len(), kind) == 0 }
}
fn poll() -> i32 {
    unsafe { horizon_uwp_dialog_poll() }
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
    Play(AuthSession),
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
                // AuthSession::parse returns fixed errors and never echoes the JSON.
                return match AuthSession::parse(&session.into_json()) {
                    Ok(session) => Outcome::Play(session),
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
                    "The account service could not be reached. Choose Play offline to continue without an online session.",
                );
                return Outcome::Menu;
            }
            Ok(Event::NoAccount) => {
                if !dismiss() {
                    return Outcome::Exit;
                }
                notice("No saved account was found. Choose Sign in to add one.");
                return Outcome::Menu;
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
                return Outcome::Menu;
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

/// None means the window was closed; Some(None) starts an offline game.
pub fn start() -> Option<Option<AuthSession>> {
    // Do not show modal UI behind the splash screen. Some hosts are already
    // visible before event delivery, so bound the wait and let ShowAsync fail
    // safely rather than leaving an unresponsive window.
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
    let client_id = option_env!("HORIZON_MSA_CLIENT_ID")
        .map(str::trim)
        .filter(|id| !id.is_empty());
    loop {
        match prompt("Choose how to start Horizon.", MENU) {
            1 => return Some(None),
            2 => {
                let outcome = match prompt("Manage the Microsoft account for this app.", ACCOUNT) {
                    1 => match client_id {
                        Some(id) => {
                            if prompt(
                                "A successful sign-in replaces this app's saved account. Continue?",
                                REPLACE,
                            ) != 1
                            {
                                Outcome::Menu
                            } else {
                                match sign_in::start(id.to_owned(), Arc::clone(&store)) {
                                    Ok(attempt) => wait(attempt),
                                    Err(error) => {
                                        notice(&error.to_string());
                                        Outcome::Menu
                                    }
                                }
                            }
                        }
                        None => {
                            notice(
                                "Microsoft sign-in is not configured for this build. Configure a Microsoft application ID in the builder.",
                            );
                            Outcome::Menu
                        }
                    },
                    2 => match prompt("Use or remove the saved account?", SAVED_ACCOUNT) {
                        1 => wait(sign_in::restore(
                            client_id.map(str::to_owned),
                            Arc::clone(&store),
                        )),
                        2 => {
                            if prompt(
                                "Remove this app's saved account from Credential Locker?",
                                CONFIRM,
                            ) == 1
                            {
                                wait(sign_in::sign_out(Arc::clone(&store)))
                            } else {
                                Outcome::Menu
                            }
                        }
                        _ => Outcome::Menu,
                    },
                    _ => Outcome::Menu,
                };
                match outcome {
                    Outcome::Play(session) => return Some(Some(session)),
                    Outcome::Exit => return None,
                    Outcome::Menu => {}
                }
            }
            _ => return None,
        }
    }
}
