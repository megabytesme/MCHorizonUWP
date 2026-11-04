//! Bounded status data for the UWP server page and live tile.

const MAX_ICON: usize = 48 * 1024;

fn icon(json: &serde_json::Value) -> Option<Vec<u8>> {
    let encoded = json["favicon"]
        .as_str()?
        .strip_prefix("data:image/png;base64,")?;
    if encoded.len() > MAX_ICON * 2 {
        return None;
    }
    let bytes = horizon_client::skins::base64_decode(&encoded.replace('\n', ""))?;
    // Validate the PNG signature and IHDR dimensions before passing untrusted
    // server data to a platform image decoder.
    if bytes.len() > MAX_ICON
        || bytes.len() < 24
        || &bytes[..8] != b"\x89PNG\r\n\x1a\n"
        || &bytes[12..16] != b"IHDR"
        || bytes[16..24] != [0, 0, 0, 64, 0, 0, 0, 64]
    {
        return None;
    }
    Some(bytes)
}

fn payload(json: &serde_json::Value) -> Vec<u8> {
    let motd = horizon_client::component::to_text(&json["description"], &mut |s| {
        horizon_client::text::Text::literal(s)
    })
    .resolve_legacy()
    .plain();
    let motd: String = motd
        .chars()
        .filter(|c| !c.is_control() || *c == '\n')
        .take(512)
        .collect();
    let players = match (
        json["players"]["online"].as_i64(),
        json["players"]["max"].as_i64(),
    ) {
        (Some(online), Some(max)) if online >= 0 && max >= 0 => format!("{online}/{max} players"),
        _ => "Player count unavailable".into(),
    };
    let icon = icon(json).unwrap_or_default();
    let mut out = Vec::with_capacity(8 + motd.len() + players.len() + icon.len());
    out.extend_from_slice(&(motd.len() as u16).to_le_bytes());
    out.extend_from_slice(&(players.len() as u16).to_le_bytes());
    out.extend_from_slice(&(icon.len() as u32).to_le_bytes());
    out.extend_from_slice(motd.as_bytes());
    out.extend_from_slice(players.as_bytes());
    out.extend_from_slice(&icon);
    out
}

#[unsafe(no_mangle)]
#[cfg(target_vendor = "uwp")]
pub unsafe extern "C" fn horizon_uwp_server_status(
    address: *const u8,
    address_len: usize,
    output: *mut u8,
    capacity: usize,
) -> isize {
    if address.is_null() || output.is_null() || address_len == 0 || address_len > 1024 {
        return -1;
    }
    let address =
        match std::str::from_utf8(unsafe { std::slice::from_raw_parts(address, address_len) }) {
            Ok(value) => value,
            Err(_) => return -1,
        };
    let (json, _) = match horizon_client::net::ping(address, std::time::Duration::from_secs(5)) {
        Ok(result) => result,
        Err(_) => return -1,
    };
    let bytes = payload(&json);
    if bytes.len() > capacity {
        return -1;
    }
    unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), output, bytes.len()) };
    bytes.len() as isize
}

#[unsafe(no_mangle)]
#[cfg(target_vendor = "uwp")]
pub unsafe extern "C" fn horizon_uwp_world_summary(
    path: *const u16,
    path_len: usize,
    output: *mut u8,
    capacity: usize,
) -> isize {
    use std::os::windows::ffi::OsStringExt;
    if path.is_null() || output.is_null() || path_len == 0 || path_len > 32767 {
        return -1;
    }
    let path = std::path::PathBuf::from(std::ffi::OsString::from_wide(unsafe {
        std::slice::from_raw_parts(path, path_len)
    }));
    let Some(level) = horizon_client::save::Level::read_summary(&path) else {
        return -1;
    };
    let mode = if level.hardcore {
        "Hardcore"
    } else {
        match level.mode() {
            1 => "Creative",
            2 => "Adventure",
            3 => "Spectator",
            _ => "Survival",
        }
    };
    let detail = format!(
        "{} · {} · Day {}",
        mode,
        level.difficulty,
        level.day_time.max(0) / 24_000 + 1
    );
    let bytes = detail.as_bytes();
    if bytes.len() > capacity {
        return -1;
    }
    unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), output, bytes.len()) };
    bytes.len() as isize
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn status_has_bounded_text_and_correct_player_count() {
        let json = serde_json::json!({"description": {"text": "Hello", "extra": [{"text":" world"}]}, "players": {"online": 3, "max": 20}});
        let out = payload(&json);
        let text_len = u16::from_le_bytes([out[0], out[1]]) as usize;
        let players_len = u16::from_le_bytes([out[2], out[3]]) as usize;
        assert_eq!(&out[8..8 + text_len], b"Hello world");
        assert_eq!(
            &out[8 + text_len..8 + text_len + players_len],
            b"3/20 players"
        );
        assert_eq!(&out[4..8], &[0, 0, 0, 0]);
    }

    #[test]
    fn invalid_or_wrong_sized_icons_are_rejected() {
        assert!(icon(&serde_json::json!({"favicon":"data:image/png;base64,aGVsbG8="})).is_none());
        assert!(icon(&serde_json::json!({"favicon":"https://example.com/icon.png"})).is_none());
        let mut png = b"\x89PNG\r\n\x1a\n\0\0\0\rIHDR\0\0\0\x40\0\0\0\x40".to_vec();
        let encoded = horizon_client::lan::base64(&png);
        assert_eq!(
            icon(&serde_json::json!({"favicon":format!("data:image/png;base64,{encoded}")})),
            Some(png.clone())
        );
        png[19] = 32;
        let encoded = horizon_client::lan::base64(&png);
        assert!(
            icon(&serde_json::json!({"favicon":format!("data:image/png;base64,{encoded}")}))
                .is_none()
        );
    }
}
