//! Safe facade for bounded device vibration in the UWP host.
#![cfg(target_vendor = "uwp")]

extern "C" {
    fn horizon_uwp_device_vibrate(millis: u32, intensity: f32) -> i32;
    fn horizon_uwp_device_vibration_cancel();
}

pub fn vibrate(millis: u32, intensity: f32) -> bool {
    if millis == 0 || millis > 250 || !intensity.is_finite() {
        return false;
    }
    unsafe { horizon_uwp_device_vibrate(millis, intensity.clamp(0.0, 1.0)) == 0 }
}

pub fn cancel() {
    unsafe { horizon_uwp_device_vibration_cancel() }
}
