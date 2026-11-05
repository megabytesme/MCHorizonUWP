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
    fn horizon_uwp_http_request(
        url: *const u8,
        url_len: usize,
        post: i32,
        authorization: *const u8,
        authorization_len: usize,
        content_type: *const u8,
        content_type_len: usize,
        body: *const u8,
        body_len: usize,
        limit: usize,
        http_status: *mut u32,
        data: *mut *mut u8,
        data_len: *mut usize,
    ) -> i32;
    fn horizon_uwp_http_free(data: *mut c_void);
    fn horizon_uwp_http_post_zstd(
        url: *const u8,
        url_len: usize,
        body: *const u8,
        body_len: usize,
        limit: usize,
        http_status: *mut u32,
        data: *mut *mut u8,
        data_len: *mut usize,
    ) -> i32;
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

/// An HTTPS response, including non-success status codes and their bodies.
pub struct Response {
    pub status: u32,
    pub body: Vec<u8>,
}

/// Send a zstd-compressed JSON payload without redirects, using Windows TLS.
pub fn post_zstd(url: &str, body: &[u8], limit: usize) -> Option<Response> {
    let mut data = ptr::null_mut();
    let mut len = 0;
    let mut status = 0;
    let result = unsafe {
        horizon_uwp_http_post_zstd(
            url.as_ptr(),
            url.len(),
            body.as_ptr(),
            body.len(),
            limit,
            &mut status,
            &mut data,
            &mut len,
        )
    };
    if result != 0 || data.is_null() || len > limit {
        if !data.is_null() {
            unsafe { horizon_uwp_http_free(data.cast()) };
        }
        return None;
    }
    let body = unsafe { std::slice::from_raw_parts(data, len) }.to_vec();
    unsafe { horizon_uwp_http_free(data.cast()) };
    Some(Response { status, body })
}

/// Perform an HTTPS GET or POST without automatic redirects.
/// This blocks and must run on a background worker. The caller controls the
/// response size limit; requests are capped at 1 MiB by the native bridge.
pub fn request(
    url: &str,
    post_body: Option<&[u8]>,
    content_type: Option<&str>,
    authorization: Option<&str>,
    limit: usize,
) -> Option<Response> {
    let body = post_body.unwrap_or_default();
    let auth = authorization.unwrap_or_default();
    if post_body.is_none() && content_type.is_some() {
        return None;
    }
    let content_type = content_type.unwrap_or_default();
    let mut data = ptr::null_mut();
    let mut len = 0;
    let mut http_status = 0;
    let result = unsafe {
        horizon_uwp_http_request(
            url.as_ptr(),
            url.len(),
            i32::from(post_body.is_some()),
            auth.as_ptr(),
            auth.len(),
            content_type.as_ptr(),
            content_type.len(),
            body.as_ptr(),
            body.len(),
            limit,
            &mut http_status,
            &mut data,
            &mut len,
        )
    };
    if result != 0 || data.is_null() || len > limit {
        if !data.is_null() {
            unsafe { horizon_uwp_http_free(data.cast()) };
        }
        return None;
    }
    let body = unsafe { std::slice::from_raw_parts(data, len) }.to_vec();
    unsafe { horizon_uwp_http_free(data.cast()) };
    Some(Response {
        status: http_status,
        body,
    })
}
