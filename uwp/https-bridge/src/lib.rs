//! Narrow, bounded HTTP GET bridge for UWP background workers.
#![cfg(target_vendor = "uwp")]
use std::{ffi::c_void, ptr};

unsafe extern "C" {
    fn horizon_uwp_http_get(
        url: *const u8,
        url_len: usize,
        limit: usize,
        data: *mut *mut u8,
        data_len: *mut usize,
    ) -> i32;
    fn horizon_uwp_http_free(data: *mut c_void);
}

/// Fetch a successful HTTP response, with OS TLS verification and a hard body limit.
/// Call from a background thread: the platform HTTP operation blocks the caller.
pub fn get(url: &str, limit: usize) -> Option<Vec<u8>> {
    let mut data = ptr::null_mut();
    let mut len = 0;
    let status =
        unsafe { horizon_uwp_http_get(url.as_ptr(), url.len(), limit, &mut data, &mut len) };
    if status != 0 || data.is_null() || len > limit {
        if !data.is_null() {
            unsafe { horizon_uwp_http_free(data.cast()) };
        }
        return None;
    }
    let result = unsafe { std::slice::from_raw_parts(data, len) }.to_vec();
    unsafe { horizon_uwp_http_free(data.cast()) };
    Some(result)
}
