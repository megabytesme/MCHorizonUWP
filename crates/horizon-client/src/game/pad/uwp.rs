//! Windows.Gaming.Input controller polling and bounded motor output.
use super::{super::Game, GlyphStyle, Pads};
use horizon_client::gamepad::{self, PadState, Pulse, Rumble};
use serde_json::{Value, json};
use std::time::{Duration, Instant};

#[derive(Default)]
pub(super) struct Devices {
    connected: u32,
    rumble_until: Option<Instant>,
}

impl Pads {
    pub(super) fn read(&mut self) -> PadState {
        let Some(reading) = horizon_uwp_gamepad::read() else {
            self.devices.connected = 0;
            self.devices.rumble_until = None;
            return PadState::default();
        };
        self.devices.connected = reading.connected;
        self.style = GlyphStyle::Xbox;
        gamepad::from_windows_gaming_input(
            reading.buttons,
            [
                reading.left_x,
                reading.left_y,
                reading.right_x,
                reading.right_y,
                reading.left_trigger,
                reading.right_trigger,
            ],
        )
    }
}

impl Game<'_> {
    pub(in crate::game) fn pad_output_off(&mut self) {
        if self.pad.devices.rumble_until.take().is_some() {
            let _ = horizon_uwp_gamepad::vibrate(0.0, 0.0, 0);
        }
    }

    pub(super) fn pad_output_frame(&mut self) {
        if !self.focused
            || self
                .pad
                .devices
                .rumble_until
                .is_some_and(|end| Instant::now() >= end)
        {
            self.pad_output_off();
        }
    }

    pub(in crate::game) fn pad_close_output(&mut self) {
        self.pad_output_off();
    }

    pub(super) fn pad_vibrate(&mut self, _: Rumble, pulse: Pulse) {
        if self.focused && horizon_uwp_gamepad::vibrate(pulse.strong, pulse.weak, pulse.millis) {
            self.pad.devices.rumble_until =
                Some(Instant::now() + Duration::from_millis(u64::from(pulse.millis)));
        }
    }

    pub(super) fn pad_devices_status(&self) -> Value {
        json!({ "backend": "windows-gaming-input", "connected": self.pad.devices.connected })
    }
}
