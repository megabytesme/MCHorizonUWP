//! Safe facade for the host's Windows.Gaming.Input bridge.
#![cfg(target_vendor = "uwp")]

/// A WinRT gamepad reading. Axes are positive right/up; triggers are 0..1.
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct Reading {
    pub buttons: u32,
    pub left_x: f32,
    pub left_y: f32,
    pub right_x: f32,
    pub right_y: f32,
    pub left_trigger: f32,
    pub right_trigger: f32,
    pub connected: u32,
}

extern "C" {
    fn horizon_uwp_gamepad_read(reading: *mut Reading) -> i32;
    fn horizon_uwp_gamepad_vibrate(strong: f32, weak: f32, millis: u32) -> i32;
}

/// The active controller, if one is available.
pub fn read() -> Option<Reading> {
    let mut reading = Reading::default();
    // The host writes one complete Reading before returning success.
    (unsafe { horizon_uwp_gamepad_read(&mut reading) } == 1).then_some(reading)
}

/// Set the active controller's motors. Invalid values are refused.
pub fn vibrate(strong: f32, weak: f32, millis: u32) -> bool {
    if !strong.is_finite() || !weak.is_finite() || millis > 2000 {
        return false;
    }
    unsafe {
        horizon_uwp_gamepad_vibrate(strong.clamp(0.0, 1.0), weak.clamp(0.0, 1.0), millis) == 0
    }
}
