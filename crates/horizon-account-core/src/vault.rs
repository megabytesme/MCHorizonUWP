//! One active account in an OS-provided credential store. No plaintext fallback.
use crate::{Account, Secrets};
use serde::{Deserialize, Serialize};
use std::fmt;

const ACTIVE_ACCOUNT: &str = "active-account-v1";
const SCHEMA: u32 = 1;
const MAX_SECRET_BYTES: usize = 65536;

/// A storage error that never includes the account's stored contents.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct StoreError;
impl fmt::Display for StoreError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("Could not access the secure account store")
    }
}
impl std::error::Error for StoreError {}

/// Secure storage supplied by the platform. Implementations must not log values.
pub trait SecretStore: Send + Sync {
    fn get(&self, key: &str) -> Result<Option<String>, StoreError>;
    fn set(&self, key: &str, value: &str) -> Result<(), StoreError>;
    fn remove(&self, key: &str) -> Result<bool, StoreError>;
}

/// Account metadata and secrets returned only in memory.
#[derive(Serialize, Deserialize)]
pub struct StoredAccount {
    schema: u32,
    pub account: Account,
    pub secrets: Secrets,
}
impl StoredAccount {
    pub fn new(account: Account, secrets: Secrets) -> Self {
        Self {
            schema: SCHEMA,
            account,
            secrets,
        }
    }
}
impl fmt::Debug for StoredAccount {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("StoredAccount")
            .field("account", &self.account)
            .field("secrets", &"<hidden>")
            .finish()
    }
}

/// Reads the active account; a missing entry is distinct from an invalid entry.
pub fn load(store: &(impl SecretStore + ?Sized)) -> Result<Option<StoredAccount>, StoreError> {
    let Some(value) = store.get(ACTIVE_ACCOUNT)? else {
        return Ok(None);
    };
    decode(&value).map(Some)
}

pub(crate) fn decode(value: &str) -> Result<StoredAccount, StoreError> {
    if value.len() > MAX_SECRET_BYTES {
        return Err(StoreError);
    }
    let account: StoredAccount = serde_json::from_str(value).map_err(|_| StoreError)?;
    if account.schema != SCHEMA {
        return Err(StoreError);
    }
    Ok(account)
}

/// Saves a complete account as one vault entry. Errors never fall back to disk.
pub fn save(
    store: &(impl SecretStore + ?Sized),
    account: &StoredAccount,
) -> Result<(), StoreError> {
    if account.schema != SCHEMA {
        return Err(StoreError);
    }
    let value = serde_json::to_string(account).map_err(|_| StoreError)?;
    if value.len() > MAX_SECRET_BYTES {
        return Err(StoreError);
    }
    store.set(ACTIVE_ACCOUNT, &value)
}

pub fn remove(store: &(impl SecretStore + ?Sized)) -> Result<bool, StoreError> {
    store.remove(ACTIVE_ACCOUNT)
}

/// Windows PasswordVault implementation, scoped to the UWP package identity.
#[cfg(target_vendor = "uwp")]
pub struct CredentialLocker;
#[cfg(target_vendor = "uwp")]
impl SecretStore for CredentialLocker {
    fn get(&self, key: &str) -> Result<Option<String>, StoreError> {
        horizon_uwp_credential_store::get(key)
            .map(|value| value.map(|secret| secret.expose().to_owned()))
            .map_err(|_| StoreError)
    }
    fn set(&self, key: &str, value: &str) -> Result<(), StoreError> {
        horizon_uwp_credential_store::set(key, value).map_err(|_| StoreError)
    }
    fn remove(&self, key: &str) -> Result<bool, StoreError> {
        horizon_uwp_credential_store::remove(key).map_err(|_| StoreError)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    #[derive(Default)]
    struct MemoryStore {
        value: Mutex<Option<String>>,
        fail: bool,
    }
    impl SecretStore for MemoryStore {
        fn get(&self, _key: &str) -> Result<Option<String>, StoreError> {
            if self.fail {
                return Err(StoreError);
            }
            Ok(self.value.lock().unwrap().clone())
        }
        fn set(&self, _key: &str, value: &str) -> Result<(), StoreError> {
            if self.fail {
                return Err(StoreError);
            }
            *self.value.lock().unwrap() = Some(value.to_owned());
            Ok(())
        }
        fn remove(&self, _key: &str) -> Result<bool, StoreError> {
            if self.fail {
                return Err(StoreError);
            }
            Ok(self.value.lock().unwrap().take().is_some())
        }
    }
    fn example() -> StoredAccount {
        let account = Account {
            uuid: "00000000000000000000000000000001".into(),
            name: "Tester".into(),
            skin: None,
            cape: None,
        };
        let secrets = serde_json::from_value(serde_json::json!({"refresh_token":"synthetic-refresh","minecraft_token":"synthetic-token","minecraft_expires_at":123,"owns":true})).unwrap();
        StoredAccount::new(account, secrets)
    }
    #[test]
    fn round_trip_and_remove_keep_secrets_out_of_debug() {
        let store = MemoryStore::default();
        assert!(load(&store).unwrap().is_none());
        save(&store, &example()).unwrap();
        let result = load(&store).unwrap().unwrap();
        assert_eq!(result.account.name, "Tester");
        assert!(!format!("{result:?}").contains("synthetic"));
        assert!(remove(&store).unwrap());
        assert!(!remove(&store).unwrap());
    }
    #[test]
    fn invalid_and_unavailable_store_fail_without_echoing_contents() {
        let store = MemoryStore::default();
        *store.value.lock().unwrap() = Some("synthetic-secret-invalid-json".into());
        let error = load(&store).unwrap_err();
        assert!(!error.to_string().contains("synthetic"));
        let mut future = serde_json::to_value(example()).unwrap();
        future["schema"] = serde_json::json!(2);
        *store.value.lock().unwrap() = Some(future.to_string());
        assert!(load(&store).is_err());
        *store.value.lock().unwrap() = Some("x".repeat(MAX_SECRET_BYTES + 1));
        assert!(load(&store).is_err());
        let failed = MemoryStore {
            fail: true,
            ..Default::default()
        };
        assert!(load(&failed).is_err());
        assert!(save(&failed, &example()).is_err());
        assert!(remove(&failed).is_err());
        assert!(failed.value.lock().unwrap().is_none());
    }
}
