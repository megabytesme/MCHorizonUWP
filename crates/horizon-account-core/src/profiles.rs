//! Multiple local profiles. Online secrets remain in the platform vault.
use crate::vault::{self, SecretStore, StoreError};
use serde::{Deserialize, Serialize};
use std::sync::{Arc, Mutex};

const INDEX: &str = "profile-index-v2";
const LEGACY: &str = "active-account-v1";
const LIMIT: usize = 16;
static INDEX_OPERATION: Mutex<()> = Mutex::new(());

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct Profile {
    pub id: String,
    pub name: String,
    pub online: bool,
    #[serde(default)]
    pub skin: Option<String>,
}

#[derive(Default, Serialize, Deserialize)]
struct Index {
    profiles: Vec<Profile>,
    #[serde(default)]
    selected: Option<String>,
}

fn online_id(uuid: &str) -> Result<String, StoreError> {
    let compact: String = uuid.chars().filter(|c| *c != '-').collect();
    if compact.len() != 32 || !compact.bytes().all(|c| c.is_ascii_hexdigit()) {
        return Err(StoreError);
    }
    Ok(format!("online:{}", compact.to_ascii_lowercase()))
}

pub fn valid_offline_name(name: &str) -> bool {
    (3..=16).contains(&name.len()) && name.bytes().all(|c| c.is_ascii_alphanumeric() || c == b'_')
}

/// Only Minecraft's public texture host is allowed. Upgrade its older HTTP
/// URLs to HTTPS and reject paths, queries, redirects and arbitrary hosts.
pub fn safe_skin_url(url: &str) -> Option<String> {
    let hash = url
        .strip_prefix("https://textures.minecraft.net/texture/")
        .or_else(|| url.strip_prefix("http://textures.minecraft.net/texture/"))?;
    if hash.is_empty() || hash.len() > 128 || !hash.bytes().all(|b| b.is_ascii_hexdigit()) {
        return None;
    }
    Some(format!("https://textures.minecraft.net/texture/{hash}"))
}

fn key(id: &str) -> String {
    format!("profile-v2-{id}")
}

fn read_index(store: &dyn SecretStore) -> Result<Index, StoreError> {
    if let Some(json) = store.get(INDEX)? {
        if json.len() > 65536 {
            return Err(StoreError);
        }
        let index: Index = serde_json::from_str(&json).map_err(|_| StoreError)?;
        if index.profiles.len() > LIMIT
            || index
                .selected
                .as_ref()
                .is_some_and(|id| !index.profiles.iter().any(|p| &p.id == id))
            || index.profiles.iter().enumerate().any(|(i, p)| {
                p.name.is_empty()
                    || p.name.len() > 64
                    || p.skin
                        .as_ref()
                        .is_some_and(|url| safe_skin_url(url).as_ref() != Some(url))
                    || index.profiles[..i].iter().any(|v| v.id == p.id)
                    || if p.online {
                        !p.id.starts_with("online:")
                            || online_id(&p.id[7..]).ok().as_ref() != Some(&p.id)
                    } else {
                        !valid_offline_name(&p.name) || p.id != format!("offline:{}", p.name)
                    }
            })
        {
            return Err(StoreError);
        }
        return Ok(index);
    }
    // Copy the old entry first and retain it. If either new write fails, the
    // previously working account is still available under its original key.
    let mut index = Index::default();
    if let Some(raw) = store.get(LEGACY)? {
        let account = vault::decode(&raw)?;
        let id = online_id(&account.account.uuid)?;
        store.set(&key(&id), &raw)?;
        index.profiles.push(Profile {
            id: id.clone(),
            name: account.account.name,
            online: true,
            skin: account
                .account
                .skin
                .as_ref()
                .and_then(|(url, _)| safe_skin_url(url)),
        });
        index.selected = Some(id);
    }
    write_index(store, &index)?;
    Ok(index)
}

fn write_index(store: &dyn SecretStore, index: &Index) -> Result<(), StoreError> {
    let json = serde_json::to_string(index).map_err(|_| StoreError)?;
    if json.len() > 65536 {
        return Err(StoreError);
    }
    store.set(INDEX, &json)
}

pub fn list(store: &dyn SecretStore) -> Result<Vec<Profile>, StoreError> {
    let _guard = INDEX_OPERATION.lock().map_err(|_| StoreError)?;
    Ok(read_index(store)?.profiles)
}

pub fn selected(store: &dyn SecretStore) -> Result<Option<Profile>, StoreError> {
    let _guard = INDEX_OPERATION.lock().map_err(|_| StoreError)?;
    let index = read_index(store)?;
    Ok(index
        .selected
        .and_then(|id| index.profiles.into_iter().find(|p| p.id == id)))
}

pub fn select(store: &dyn SecretStore, id: &str) -> Result<(), StoreError> {
    let _guard = INDEX_OPERATION.lock().map_err(|_| StoreError)?;
    let mut index = read_index(store)?;
    if !index.profiles.iter().any(|p| p.id == id) {
        return Err(StoreError);
    }
    index.selected = Some(id.to_owned());
    write_index(store, &index)
}

pub fn add_offline(store: &dyn SecretStore, name: &str) -> Result<Profile, StoreError> {
    if !valid_offline_name(name) {
        return Err(StoreError);
    }
    let _guard = INDEX_OPERATION.lock().map_err(|_| StoreError)?;
    let mut index = read_index(store)?;
    let profile = Profile {
        id: format!("offline:{name}"),
        name: name.to_owned(),
        online: false,
        skin: None,
    };
    if !index.profiles.iter().any(|p| p.id == profile.id) {
        if index.profiles.len() >= LIMIT {
            return Err(StoreError);
        }
        index.profiles.push(profile.clone());
        write_index(store, &index)?;
    }
    Ok(profile)
}

/// A per-profile view of the existing sign-in code's single active-account
/// key. The add mode indexes the account only after its secret is in the vault.
pub struct ProfileStore {
    inner: Arc<dyn SecretStore>,
    selected: Option<String>,
}
impl ProfileStore {
    pub fn adding(inner: Arc<dyn SecretStore>) -> Self {
        Self {
            inner,
            selected: None,
        }
    }
    pub fn selected(inner: Arc<dyn SecretStore>, id: String) -> Result<Self, StoreError> {
        if !id.starts_with("online:") || online_id(&id[7..])? != id {
            return Err(StoreError);
        }
        Ok(Self {
            inner,
            selected: Some(id),
        })
    }
}
impl SecretStore for ProfileStore {
    fn get(&self, name: &str) -> Result<Option<String>, StoreError> {
        if name != LEGACY {
            return Err(StoreError);
        }
        match &self.selected {
            Some(id) => self.inner.get(&key(id)),
            None => Ok(None),
        }
    }
    fn set(&self, name: &str, value: &str) -> Result<(), StoreError> {
        if name != LEGACY || value.len() > 65536 {
            return Err(StoreError);
        }
        let account = vault::decode(value)?;
        let id = online_id(&account.account.uuid)?;
        if self
            .selected
            .as_ref()
            .is_some_and(|selected| selected != &id)
        {
            return Err(StoreError);
        }
        let _guard = INDEX_OPERATION.lock().map_err(|_| StoreError)?;
        let mut index = read_index(self.inner.as_ref())?;
        if !index.profiles.iter().any(|p| p.id == id) && index.profiles.len() >= LIMIT {
            return Err(StoreError);
        }
        self.inner.set(&key(&id), value)?;
        if let Some(profile) = index.profiles.iter_mut().find(|p| p.id == id) {
            profile.name = account.account.name;
            profile.skin = account
                .account
                .skin
                .as_ref()
                .and_then(|(url, _)| safe_skin_url(url));
        } else {
            index.profiles.push(Profile {
                id,
                name: account.account.name,
                online: true,
                skin: account
                    .account
                    .skin
                    .as_ref()
                    .and_then(|(url, _)| safe_skin_url(url)),
            });
        }
        write_index(self.inner.as_ref(), &index)
    }
    fn remove(&self, name: &str) -> Result<bool, StoreError> {
        if name != LEGACY {
            return Err(StoreError);
        }
        let Some(id) = &self.selected else {
            return Ok(false);
        };
        remove(self.inner.as_ref(), id)
    }
}

/// Only called after the user confirms removing this profile.
pub fn remove(store: &dyn SecretStore, id: &str) -> Result<bool, StoreError> {
    let _guard = INDEX_OPERATION.lock().map_err(|_| StoreError)?;
    let mut index = read_index(store)?;
    let Some(position) = index.profiles.iter().position(|p| p.id == id) else {
        return Ok(false);
    };
    if index.profiles[position].online {
        store.remove(&key(id))?;
        if let Some(legacy) = vault::load(store)? {
            if online_id(&legacy.account.uuid)? == id {
                store.remove(LEGACY)?;
            }
        }
    }
    index.profiles.remove(position);
    if index.selected.as_deref() == Some(id) {
        index.selected = None;
    }
    write_index(store, &index)?;
    Ok(true)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::Account;
    use std::collections::HashMap;

    #[derive(Default)]
    struct MemoryStore {
        values: Mutex<HashMap<String, String>>,
        fail_write: Mutex<Option<String>>,
    }
    impl SecretStore for MemoryStore {
        fn get(&self, key: &str) -> Result<Option<String>, StoreError> {
            Ok(self.values.lock().unwrap().get(key).cloned())
        }
        fn set(&self, key: &str, value: &str) -> Result<(), StoreError> {
            if self.fail_write.lock().unwrap().as_deref() == Some(key) {
                return Err(StoreError);
            }
            self.values.lock().unwrap().insert(key.into(), value.into());
            Ok(())
        }
        fn remove(&self, key: &str) -> Result<bool, StoreError> {
            Ok(self.values.lock().unwrap().remove(key).is_some())
        }
    }
    fn account(uuid: &str, name: &str, token: &str) -> vault::StoredAccount {
        let account = Account {
            uuid: uuid.into(),
            name: name.into(),
            skin: None,
            cape: None,
        };
        let secrets = serde_json::from_value(serde_json::json!({
            "refresh_token":token,"minecraft_token":"synthetic-minecraft-token",
            "minecraft_expires_at":123,"owns":true
        }))
        .unwrap();
        vault::StoredAccount::new(account, secrets)
    }
    #[test]
    fn migration_preserves_the_original_until_explicit_removal() {
        let store = MemoryStore::default();
        vault::save(
            &store,
            &account("00000000000000000000000000000001", "First", "synthetic-one"),
        )
        .unwrap();
        let before = store.get(LEGACY).unwrap().unwrap();
        let profiles = list(&store).unwrap();
        assert_eq!(profiles.len(), 1);
        assert_eq!(selected(&store).unwrap(), Some(profiles[0].clone()));
        assert_eq!(store.get(LEGACY).unwrap().as_deref(), Some(before.as_str()));
        assert_eq!(
            store.get(&key(&profiles[0].id)).unwrap().as_deref(),
            Some(before.as_str())
        );
        assert!(remove(&store, &profiles[0].id).unwrap());
        assert!(store.get(LEGACY).unwrap().is_none());
        assert!(list(&store).unwrap().is_empty());
    }
    #[test]
    fn failed_migration_keeps_the_legacy_credential() {
        let store = MemoryStore::default();
        vault::save(
            &store,
            &account("00000000000000000000000000000001", "First", "synthetic-one"),
        )
        .unwrap();
        let before = store.get(LEGACY).unwrap().unwrap();
        *store.fail_write.lock().unwrap() = Some(INDEX.into());
        assert!(list(&store).is_err());
        assert_eq!(store.get(LEGACY).unwrap().as_deref(), Some(before.as_str()));
        *store.fail_write.lock().unwrap() = None;
        assert_eq!(list(&store).unwrap().len(), 1);
    }
    #[test]
    fn accounts_are_isolated_and_offline_names_are_validated() {
        let store = Arc::new(MemoryStore::default());
        let inner: Arc<dyn SecretStore> = store.clone();
        let adding = ProfileStore::adding(inner.clone());
        vault::save(
            &adding,
            &account("00000000000000000000000000000001", "First", "synthetic-one"),
        )
        .unwrap();
        vault::save(
            &adding,
            &account(
                "00000000000000000000000000000002",
                "Second",
                "synthetic-two",
            ),
        )
        .unwrap();
        assert!(add_offline(store.as_ref(), "Bad name!").is_err());
        let offline = add_offline(store.as_ref(), "Offline_Player").unwrap();
        let profiles = list(store.as_ref()).unwrap();
        assert_eq!(profiles.len(), 3);
        assert!(!offline.online);
        select(store.as_ref(), &offline.id).unwrap();
        assert_eq!(selected(store.as_ref()).unwrap(), Some(offline.clone()));
        let first = ProfileStore::selected(inner.clone(), profiles[0].id.clone()).unwrap();
        let second = ProfileStore::selected(inner, profiles[1].id.clone()).unwrap();
        assert_eq!(vault::load(&first).unwrap().unwrap().account.name, "First");
        assert_eq!(
            vault::load(&second).unwrap().unwrap().account.name,
            "Second"
        );
        assert!(vault::remove(&first).unwrap());
        assert!(vault::load(&first).unwrap().is_none());
        assert_eq!(
            vault::load(&second).unwrap().unwrap().account.name,
            "Second"
        );
        assert_eq!(list(store.as_ref()).unwrap().len(), 2);
        assert_eq!(selected(store.as_ref()).unwrap(), Some(offline));
    }

    #[test]
    fn skin_urls_are_limited_to_minecraft_textures_over_https() {
        assert_eq!(
            safe_skin_url("http://textures.minecraft.net/texture/abc123").as_deref(),
            Some("https://textures.minecraft.net/texture/abc123")
        );
        for url in [
            "https://evil.example/texture/abc123",
            "https://textures.minecraft.net.evil.example/texture/abc123",
            "https://textures.minecraft.net/texture/abc123?token=secret",
            "https://textures.minecraft.net/texture/../abc",
        ] {
            assert!(safe_skin_url(url).is_none());
        }
    }
}
