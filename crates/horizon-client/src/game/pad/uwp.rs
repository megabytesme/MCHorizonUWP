//! UWP controller input through Windows.Gaming.Input via gilrs.
//! Raw HID gamepads use gilrs' bundled SDL mappings and generic fallback.
use super::{super::Game, PadEvent, Pads};
use horizon_client::gamepad::{self, Button, GlyphStyle, PadState, Pulse, Rumble};
use serde_json::{Value, json};
use std::time::{Duration, Instant};

#[derive(Default)]
pub(super) struct Devices {
    gilrs: Option<gilrs::Gilrs>,
    started: bool,
    active: Option<gilrs::GamepadId>,
    rumbles: Vec<(gilrs::ff::Effect, Instant)>,
}

use crate::uwp_controller::new_gilrs;

fn gilrs_button(button: Button) -> gilrs::Button {
    use gilrs::Button as G;
    match button {
        Button::South => G::South,
        Button::East => G::East,
        Button::West => G::West,
        Button::North => G::North,
        Button::LeftBumper => G::LeftTrigger,
        Button::RightBumper => G::RightTrigger,
        Button::LeftTrigger => G::LeftTrigger2,
        Button::RightTrigger => G::RightTrigger2,
        Button::Select => G::Select,
        Button::Start => G::Start,
        Button::LeftStick => G::LeftThumb,
        Button::RightStick => G::RightThumb,
        Button::DPadUp => G::DPadUp,
        Button::DPadDown => G::DPadDown,
        Button::DPadLeft => G::DPadLeft,
        Button::DPadRight => G::DPadRight,
    }
}

impl Pads {
    fn start(&mut self) {
        if self.devices.started {
            return;
        }
        self.devices.started = true;
        self.devices.gilrs = match new_gilrs() {
            Ok(g) => Some(g),
            Err(error) => {
                log::warn!("Horizon: UWP controllers are unavailable: {error}");
                None
            }
        };
    }

    pub(in crate::game) fn release_hardware(&mut self) {
        self.devices.rumbles.clear();
        self.devices.gilrs = None;
        self.devices.started = false;
        self.devices.active = None;
    }

    pub(super) fn read(&mut self) -> PadState {
        self.start();
        let Some(gilrs) = self.devices.gilrs.as_mut() else {
            return PadState::default();
        };
        while let Some(gilrs::Event { id, event, .. }) = gilrs.next_event() {
            use gilrs::EventType as E;
            match event {
                E::Connected => log::info!("Controller connected: {}", gilrs.gamepad(id).name()),
                E::Disconnected => {
                    log::info!("Controller disconnected: {}", gilrs.gamepad(id).name());
                    if self.devices.active == Some(id) {
                        self.devices.active = None;
                    }
                }
                E::ButtonPressed(..) => self.devices.active = Some(id),
                E::AxisChanged(_, value, _) | E::ButtonChanged(_, value, _)
                    if value.abs() > 0.5 =>
                {
                    self.devices.active = Some(id)
                }
                _ => {}
            }
        }
        let Some(pad) = self
            .devices
            .active
            .and_then(|id| gilrs.connected_gamepad(id))
        else {
            return PadState::default();
        };
        self.style = GlyphStyle::of_vendor(pad.vendor_id());
        let mut state = PadState::default();
        for button in Button::ALL {
            if !matches!(button, Button::LeftTrigger | Button::RightTrigger) {
                state.set(button, pad.is_pressed(gilrs_button(button)));
            }
        }
        use gilrs::Axis as A;
        let axis = |a| pad.value(a);
        let (left, right) = gamepad::stick_axes(
            [axis(A::LeftStickX), axis(A::LeftStickY)],
            [axis(A::RightStickX), axis(A::RightStickY)],
            axis(A::LeftZ),
            pad.vendor_id(),
            pad.product_id(),
            pad.mapping_source() == gilrs::MappingSource::SdlMappings,
        );
        state.left = left;
        state.right = right;
        let trigger = |b: gilrs::Button| {
            let value = pad.button_data(b).map_or(0.0, |d| d.value());
            if pad.is_pressed(b) {
                value.max(1.0)
            } else {
                value
            }
        };
        state.triggers = [
            trigger(gilrs::Button::LeftTrigger2),
            trigger(gilrs::Button::RightTrigger2),
        ];
        state
    }
}

/// A pad's buttons, sticks and triggers now.
fn state_of(pad: &gilrs::Gamepad) -> PadState {
    let mut state = PadState::default();
    for button in Button::ALL {
        if !matches!(button, Button::LeftTrigger | Button::RightTrigger) {
            state.set(button, pad.is_pressed(gilrs_button(button)));
        }
    }
    let axis = |a| pad.value(a);
    use gilrs::Axis as A;
    let sdl_mapped = pad.mapping_source() == gilrs::MappingSource::SdlMappings;
    let (left, right) = gamepad::stick_axes(
        [axis(A::LeftStickX), axis(A::LeftStickY)],
        [axis(A::RightStickX), axis(A::RightStickY)],
        axis(A::LeftZ),
        pad.vendor_id(),
        pad.product_id(),
        sdl_mapped,
    );
    state.left = left;
    state.right = right;
    // Triggers arrive as analog buttons on most mappings, as plain
    // buttons on some.
    let trigger = |b: gilrs::Button| {
        let value = pad.button_data(b).map_or(0.0, |d| d.value());
        if pad.is_pressed(b) {
            value.max(1.0)
        } else {
            value
        }
    };
    state.triggers = [
        trigger(gilrs::Button::LeftTrigger2),
        trigger(gilrs::Button::RightTrigger2),
    ];
    state
}

/// A controller, as splitscreen tells them apart.
pub(in crate::game) type PadId = gilrs::GamepadId;

/// Splitscreen: one gilrs for every player's game in this process. The split decides which
/// player each pad drives; this reads them.
#[derive(Default)]
pub(in crate::game) struct PadRouter {
    gilrs: Option<gilrs::Gilrs>,
    started: bool,
}

impl PadRouter {
    /// Solo play uses this reader's device IDs for feedback too: another pad cannot steal
    /// the active output while its input is being offered to join. Reclaimed after P1's turn.
    pub(in crate::game) fn lend_output(&mut self, pads: &mut Pads, id: PadId) {
        if pads.devices.gilrs.is_some() {
            pads.release_hardware();
        }
        if pads.devices.active != Some(id) {
            pads.devices.rumbles.clear();
        }
        pads.devices.active = Some(id);
        pads.devices.started = true;
        pads.devices.gilrs = self.gilrs.take();
    }

    pub(in crate::game) fn reclaim_output(&mut self, pads: &mut Pads) {
        self.gilrs = pads.devices.gilrs.take();
    }

    /// The buttons pressed and the controllers gone or back since the last call, in order.
    pub(in crate::game) fn events(&mut self) -> Vec<PadEvent> {
        if !self.started {
            self.started = true;
            self.gilrs = match new_gilrs() {
                Ok(gilrs) => Some(gilrs),
                Err(error) => {
                    log::warn!("Horizon: UWP controllers are unavailable: {error}");
                    None
                }
            };
        }
        let Some(gilrs) = self.gilrs.as_mut() else {
            return Vec::new();
        };
        let mut events = Vec::new();
        while let Some(gilrs::Event { id, event, .. }) = gilrs.next_event() {
            match event {
                gilrs::EventType::ButtonPressed(pressed, _) => {
                    if let Some(button) = Button::ALL
                        .into_iter()
                        .find(|&b| gilrs_button(b) == pressed)
                    {
                        events.push(PadEvent::Press(id, button));
                    }
                }
                gilrs::EventType::Disconnected => {
                    log::info!("Controller disconnected: {}", gilrs.gamepad(id).name());
                    events.push(PadEvent::Gone(id));
                }
                gilrs::EventType::Connected => events.push(PadEvent::Back(id)),
                _ => {}
            }
        }
        events
    }

    pub(in crate::game) fn connected(&self, id: PadId) -> bool {
        self.gilrs
            .as_ref()
            .is_some_and(|g| g.connected_gamepad(id).is_some())
    }

    /// The kind of controller (its maker, model and bus; two of one model share it), to know it
    /// back when it reconnects under a new id; zero when the system doesn't say.
    pub(in crate::game) fn uuid(&self, id: PadId) -> [u8; 16] {
        self.gilrs
            .as_ref()
            .map_or([0; 16], |g| g.gamepad(id).uuid())
    }

    /// The pad's state now (released when it is gone).
    pub(in crate::game) fn state(&self, id: PadId) -> PadState {
        self.gilrs
            .as_ref()
            .and_then(|g| g.connected_gamepad(id))
            .map_or_else(PadState::default, |pad| state_of(&pad))
    }

    /// Its glyphs (Xbox, PlayStation, Nintendo), as `read` finds them for the active pad.
    pub(in crate::game) fn style(&self, id: PadId) -> GlyphStyle {
        self.gilrs
            .as_ref()
            .and_then(|g| g.connected_gamepad(id))
            .map_or(GlyphStyle::Auto, |pad| {
                GlyphStyle::of_vendor(pad.vendor_id())
            })
    }

    pub(in crate::game) fn name(&self, id: PadId) -> String {
        self.gilrs
            .as_ref()
            .and_then(|g| g.connected_gamepad(id))
            .map_or_else(|| "A controller".into(), |pad| pad.name().to_owned())
    }
}

impl Game<'_> {
    pub(in crate::game) fn pad_output_off(&mut self) {
        self.pad.devices.rumbles.clear();
    }
    pub(super) fn pad_output_frame(&mut self) {
        let now = Instant::now();
        self.pad.devices.rumbles.retain(|(_, end)| *end > now);
    }
    pub(in crate::game) fn pad_close_output(&mut self) {
        self.pad_output_off();
    }

    pub(super) fn pad_vibrate(&mut self, _: Rumble, pulse: Pulse) {
        let (Some(gilrs), Some(id)) = (self.pad.devices.gilrs.as_mut(), self.pad.devices.active)
        else {
            return;
        };
        if !gilrs
            .connected_gamepad(id)
            .is_some_and(|g| g.is_ff_supported())
        {
            return;
        }
        use gilrs::ff::{BaseEffect, BaseEffectType, EffectBuilder, Repeat, Replay, Ticks};
        let length = Ticks::from_ms(pulse.millis);
        let magnitude = |v: f32| (v.clamp(0.0, 1.0) * f32::from(u16::MAX)) as u16;
        let motor = |kind| BaseEffect {
            kind,
            scheduling: Replay {
                play_for: length,
                ..Default::default()
            },
            envelope: Default::default(),
        };
        let effect = EffectBuilder::new()
            .add_effect(motor(BaseEffectType::Strong {
                magnitude: magnitude(pulse.strong),
            }))
            .add_effect(motor(BaseEffectType::Weak {
                magnitude: magnitude(pulse.weak),
            }))
            .repeat(Repeat::For(length))
            .gamepads(&[id])
            .finish(gilrs);
        match effect.and_then(|e| e.play().map(|()| e)) {
            Ok(effect) => self.pad.devices.rumbles.push((
                effect,
                Instant::now() + Duration::from_millis(u64::from(pulse.millis) + 50),
            )),
            Err(error) => log::debug!("Horizon: controller vibration: {error}"),
        }
    }

    pub(super) fn pad_devices_status(&self) -> Value {
        let devices = &self.pad.devices;
        let pads: Vec<Value> = devices.gilrs.as_ref().map_or(Vec::new(), |g| {
            g.gamepads().map(|(id, p)| json!({
            "name": p.name(), "vendor": p.vendor_id(), "product": p.product_id(),
            "active": devices.active == Some(id), "mapping": format!("{:?}", p.mapping_source()),
        })).collect()
        });
        json!({ "backend": devices.gilrs.is_some(), "controllers": pads })
    }
}
