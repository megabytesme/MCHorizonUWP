//! Bounded UWP Credential Locker bridge. No credentials are logged.
#![cfg(target_vendor = "uwp")]
use std::{ffi::c_void, fmt, ptr};
unsafe extern "C" {
    fn horizon_vault_set(
        key: *const u8,
        key_len: usize,
        secret: *const u8,
        secret_len: usize,
    ) -> i32;
    fn horizon_vault_get(
        key: *const u8,
        key_len: usize,
        output: *mut *mut u8,
        output_len: *mut usize,
    ) -> i32;
    fn horizon_vault_remove(key: *const u8, key_len: usize) -> i32;
    fn horizon_vault_free(output: *mut c_void, output_len: usize);
}
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct VaultError;
impl fmt::Display for VaultError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("Credential Locker operation failed")
    }
}
impl std::error::Error for VaultError {}
/// Secret value deliberately redacts its Debug representation.
pub struct Secret(String);
impl Secret {
    pub fn expose(&self) -> &str {
        &self.0
    }
}
impl fmt::Debug for Secret {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("Secret(<hidden>)")
    }
}
/// Store a nonempty UTF-8 secret for the account key.
pub fn set(key: &str, secret: &str) -> Result<(), VaultError> {
    (unsafe { horizon_vault_set(key.as_ptr(), key.len(), secret.as_ptr(), secret.len()) } == 0)
        .then_some(())
        .ok_or(VaultError)
}
/// Read one value. Missing entries are distinct from system errors.
pub fn get(key: &str) -> Result<Option<Secret>, VaultError> {
    let mut output = ptr::null_mut();
    let mut len = 0;
    let status = unsafe { horizon_vault_get(key.as_ptr(), key.len(), &mut output, &mut len) };
    if status == 1 {
        return Ok(None);
    }
    if status != 0 || output.is_null() || len > 65536 {
        if !output.is_null() {
            unsafe { horizon_vault_free(output.cast(), len) };
        }
        return Err(VaultError);
    }
    let value = String::from_utf8(unsafe { std::slice::from_raw_parts(output, len) }.to_vec());
    unsafe { horizon_vault_free(output.cast(), len) };
    value.map(|s| Some(Secret(s))).map_err(|_| VaultError)
}
/// Remove one entry, returning false if it did not exist.
pub fn remove(key: &str) -> Result<bool, VaultError> {
    match unsafe { horizon_vault_remove(key.as_ptr(), key.len()) } {
        0 => Ok(true),
        1 => Ok(false),
        _ => Err(VaultError),
    }
}
