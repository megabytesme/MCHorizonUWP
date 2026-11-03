//! Optional native mod for the device's own vibration motor.
pub const MOD_ID: &str = "horizon_device_vibration";
pub const MOD_TITLE: &str = "Device Vibration 0.1.0";
pub const MOD_DESCRIPTION: &str =
    "Vibrate this device for game events. This setting is separate from controller vibration.";
pub const ENABLED_KEY: &str = "enabled";

pub fn enabled(setting: Option<&str>) -> bool {
    setting != Some("false")
}

/// Short, bounded device pulses; controller rumble is controlled separately.
pub fn pulse(strength: f32, millis: u32) -> Option<(f32, u32)> {
    if !strength.is_finite() || strength <= 0.05 || millis == 0 {
        return None;
    }
    Some((strength.min(1.0), millis.min(250)))
}

#[cfg(target_vendor = "uwp")]
pub fn vibrate(strength: f32, millis: u32) {
    if let Some((intensity, duration)) = pulse(strength, millis) {
        let _ = horizon_uwp_device_vibration::vibrate(duration, intensity);
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
    fn pulses_are_bounded_and_reject_invalid_input() {
        assert_eq!(pulse(0.4, 60), Some((0.4, 60)));
        assert_eq!(pulse(5.0, 900), Some((1.0, 250)));
        assert_eq!(pulse(0.0, 200), None);
        assert_eq!(pulse(f32::NAN, 200), None);
        assert_eq!(pulse(0.5, 0), None);
        assert!(enabled(None));
        assert!(enabled(Some("true")));
        assert!(!enabled(Some("false")));
    }
}
