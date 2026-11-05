//! Safe SRV lookup facade for the WinRT network bridge.
#![cfg(target_vendor = "uwp")]

unsafe extern "C" {
    fn horizon_uwp_resolve_srv(
        host: *const u8,
        host_len: usize,
        output: *mut u8,
        output_capacity: usize,
        output_len: *mut usize,
        port: *mut u16,
    ) -> i32;
}

/// Resolve one Minecraft service using the device's configured DNS.
pub fn srv(name: &str) -> Option<(String, u16)> {
    let host = name.strip_prefix("_minecraft._tcp.")?.trim_end_matches('.');
    if host.is_empty() || host.len() > 253 {
        return None;
    }
    let mut output = [0u8; 1024];
    let mut length = 0;
    let mut port = 0;
    let result = unsafe {
        horizon_uwp_resolve_srv(
            host.as_ptr(),
            host.len(),
            output.as_mut_ptr(),
            output.len(),
            &mut length,
            &mut port,
        )
    };
    if result != 0 || length == 0 || length > output.len() || port == 0 {
        return None;
    }
    let target = std::str::from_utf8(&output[..length]).ok()?;
    if target.contains('\0') {
        return None;
    }
    Some((target.to_owned(), port))
}
