//! Internal UWP device motor policy, separate from controller rumble.
#![forbid(unsafe_code)]

pub const SETTING_ID: &str = "horizon_device_vibration";

/// A device pulse is short and finite. A missing motor is harmless.
pub fn bounded_pulse(strength: f32, millis: u32) -> Option<(f32, u32)> {
    if !strength.is_finite() || strength <= 0.05 || millis == 0 {
        return None;
    }
    Some((strength.min(1.0), millis.min(250)))
}

/// Legacy switches defaulted to enabled; preserve their explicit false values.
pub fn enabled(
    setting: Option<&str>,
    while_controller: Option<&str>,
    controller_in_use: bool,
) -> bool {
    setting != Some("false") && (!controller_in_use || while_controller != Some("false"))
}

#[cfg(target_vendor = "uwp")]
pub fn vibrate(strength: f32, millis: u32) {
    if let Some((strength, millis)) = bounded_pulse(strength, millis) {
        let _ = horizon_uwp_device_vibration::vibrate(millis, strength);
    }
}
#[cfg(target_vendor = "uwp")]
pub fn cancel() {
    horizon_uwp_device_vibration::cancel();
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn pulses_and_switches_follow_device_policy() {
        assert_eq!(bounded_pulse(1.5, 300), Some((1.0, 250)));
        assert_eq!(bounded_pulse(f32::NAN, 20), None);
        assert_eq!(bounded_pulse(0.05, 20), None);
        assert!(enabled(None, None, true));
        assert!(!enabled(Some("false"), None, false));
        assert!(!enabled(None, Some("false"), true));
        assert!(enabled(None, Some("false"), false));
    }
}
