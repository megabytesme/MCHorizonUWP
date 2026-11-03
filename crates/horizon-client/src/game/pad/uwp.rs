//! Windows.Gaming.Input controller polling and bounded motor output.
use super::{super::Game, GlyphStyle, PadEvent, Pads};
use horizon_client::gamepad::{self, PadState, Pulse, Rumble};
use serde_json::{Value, json};
use std::time::{Duration, Instant};

#[derive(Default)]
pub(super) struct Devices {
    connected: u32,
    rumble_until: Option<Instant>,
}

pub(in crate::game) type PadId = usize;

/// The UWP bridge currently exposes one active controller to the split router.
#[derive(Default)]
pub(in crate::game) struct PadRouter {
    connected: bool,
    state: PadState,
}
impl PadRouter {
    pub(in crate::game) fn events(&mut self) -> Vec<PadEvent> {
        let reading = horizon_uwp_gamepad::read();
        let connected = reading.is_some();
        let next = reading.map_or_else(PadState::default, |r| {
            gamepad::from_windows_gaming_input(
                r.buttons,
                [
                    r.left_x,
                    r.left_y,
                    r.right_x,
                    r.right_y,
                    r.left_trigger,
                    r.right_trigger,
                ],
            )
        });
        let mut events = Vec::new();
        if connected != self.connected {
            events.push(if connected {
                PadEvent::Back(0)
            } else {
                PadEvent::Gone(0)
            });
        }
        if connected {
            for button in gamepad::Button::ALL {
                if next.held(button) && !self.state.held(button) {
                    events.push(PadEvent::Press(0, button));
                }
            }
        }
        self.connected = connected;
        self.state = next;
        events
    }
    pub(in crate::game) fn lend_output(&mut self, pads: &mut Pads, _: PadId) {
        pads.devices.connected = u32::from(self.connected);
    }
    pub(in crate::game) fn reclaim_output(&mut self, _: &mut Pads) {}
    pub(in crate::game) fn connected(&self, id: PadId) -> bool {
        id == 0 && self.connected
    }
    pub(in crate::game) fn uuid(&self, _: PadId) -> [u8; 16] {
        [0; 16]
    }
    pub(in crate::game) fn state(&self, id: PadId) -> PadState {
        if self.connected(id) {
            self.state
        } else {
            PadState::default()
        }
    }
    pub(in crate::game) fn name(&self, _: PadId) -> String {
        "Windows controller".into()
    }
    pub(in crate::game) fn style(&self, _: PadId) -> GlyphStyle {
        GlyphStyle::Xbox
    }
}

impl Pads {
    pub(in crate::game) fn release_hardware(&mut self) {
        if self.devices.rumble_until.take().is_some() {
            let _ = horizon_uwp_gamepad::vibrate(0.0, 0.0, 0);
        }
        self.devices.connected = 0;
    }
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
