//! Internal, statically linked UWP touch and gyrometer controls.
#![forbid(unsafe_code)]
use std::collections::HashMap;

pub const MOD_ID: &str = "horizon_touch";
pub const DEBUG_OVERLAY_ID: i32 = 15;
pub const MOVE_CONTROL_ID: i32 = 21;
pub const MOVE_HALF_EXTENT: f32 = 1.7;
#[derive(Clone, Debug, PartialEq)]
pub struct Action {
    pub id: i32,
    pub value: f32,
}
#[derive(Clone, Debug, PartialEq)]
pub struct Control {
    pub id: i32,
    pub x: f32,
    pub y: f32,
    pub radius: f32,
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct GyroSettings {
    pub enabled: bool,
    pub sensitivity: f32,
}
impl Default for GyroSettings {
    fn default() -> Self {
        Self {
            enabled: false,
            sensitivity: 1.0,
        }
    }
}
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct TouchSettings {
    pub sensitivity: f32,
    pub control_scale: f32,
}
impl Default for TouchSettings {
    fn default() -> Self {
        Self {
            sensitivity: 1.0,
            control_scale: 1.0,
        }
    }
}
fn number(value: &str, lo: f32, hi: f32) -> Option<f32> {
    let value: f32 = value.trim().parse().ok()?;
    value.is_finite().then(|| value.clamp(lo, hi))
}
impl GyroSettings {
    pub fn parse(text: &str) -> Option<Self> {
        let mut out = Self::default();
        for line in text.lines() {
            if let Some((key, value)) = line.split_once('=') {
                match key.trim() {
                    "enabled" | "gyro_enabled" => {
                        out.enabled = matches!(value.trim(), "true" | "1")
                    }
                    "sensitivity" | "gyro_sensitivity" => {
                        out.sensitivity = number(value, 0.25, 3.0)?
                    }
                    _ => {}
                }
            }
        }
        Some(out)
    }
}
impl TouchSettings {
    pub fn parse(text: &str) -> Option<Self> {
        let mut out = Self::default();
        for line in text.lines() {
            if let Some((key, value)) = line.split_once('=') {
                match key.trim() {
                    "touch_sensitivity" => out.sensitivity = number(value, 0.25, 3.0)?,
                    "control_scale" => out.control_scale = number(value, 0.6, 1.5)?,
                    _ => {}
                }
            }
        }
        Some(out)
    }
}
pub fn gyro_turn([x, y]: [f32; 2], rotation: i32) -> [f32; 2] {
    let (right, up) = match rotation.rem_euclid(4) {
        0 => (x, y),
        1 => (-y, x),
        2 => (-x, -y),
        _ => (y, -x),
    };
    [-up.to_degrees(), -right.to_degrees()]
}
#[derive(Clone, Copy)]
struct Finger {
    role: i32,
    x: f32,
    y: f32,
}
#[derive(Default)]
pub struct TouchModule {
    fingers: HashMap<i32, Finger>,
    inventory: bool,
    gyro: GyroSettings,
    touch: TouchSettings,
}
impl TouchModule {
    pub fn new() -> Self {
        Self::default()
    }
    pub fn supports_menu(&self) -> bool {
        true
    }
    pub fn set_gyro_settings(&mut self, settings: GyroSettings) {
        self.gyro = settings;
    }
    pub fn gyro_settings(&self) -> GyroSettings {
        self.gyro
    }
    pub fn set_touch_settings(&mut self, settings: TouchSettings) {
        self.touch = settings;
    }
    pub fn set_context(&mut self, inventory: bool) -> Result<(), String> {
        self.inventory = inventory;
        Ok(())
    }
    fn geometry(&self, w: f32, h: f32) -> Vec<Control> {
        let s = w.min(h);
        let k = self.touch.control_scale;
        let r = s * 0.075 * k;
        [
            (5, w - s * 0.11 * k, h - s * 0.14 * k),
            (6, w - s * 0.27 * k, h - s * 0.25 * k),
            (7, w - s * 0.11 * k, h - s * 0.36 * k),
            (8, w - s * 0.08 * k, s * 0.08 * k),
            (9, s * 0.08 * k, s * 0.08 * k),
            (10, s * 0.09 * k, h - s * 0.43 * k),
            (11, s * 0.26 * k, h - s * 0.43 * k),
            (12, w * 0.5 - s * 0.20 * k, h - s * 0.075 * k),
            (13, w * 0.5 + s * 0.20 * k, h - s * 0.075 * k),
            (MOVE_CONTROL_ID, s * 0.18 * k, h - s * 0.18 * k),
            (DEBUG_OVERLAY_ID, s * 0.28 * k, s * 0.08 * k),
        ]
        .into_iter()
        .map(|(id, x, y)| Control {
            id,
            x,
            y,
            radius: r,
        })
        .collect()
    }
    pub fn draw(&mut self, w: f32, h: f32) -> Result<Vec<Control>, String> {
        if !w.is_finite() || !h.is_finite() || w <= 0.0 || h <= 0.0 {
            return Err("invalid viewport".into());
        }
        let all = self.geometry(w, h);
        if self.inventory {
            let c = &all[4];
            Ok(vec![Control {
                id: 14,
                ..c.clone()
            }])
        } else {
            Ok(all)
        }
    }
    pub fn touch(
        &mut self,
        id: i32,
        phase: i32,
        x: f32,
        y: f32,
        w: f32,
        h: f32,
    ) -> Result<(bool, Vec<Action>), String> {
        if id < 0
            || !(0..=3).contains(&phase)
            || ![x, y, w, h].iter().all(|v| v.is_finite())
            || w <= 0.0
            || h <= 0.0
        {
            return Err("invalid input".into());
        }
        let controls = self.geometry(w, h);
        let movement = &controls[9];
        let mut actions = Vec::new();
        let mut emit = |id, value| actions.push(Action { id, value });
        match phase {
            0 => {
                if self.fingers.contains_key(&id) || self.fingers.len() >= 16 {
                    return Ok((false, actions));
                }
                let hit = |c: &Control, factor: f32| {
                    (x - c.x).powi(2) + (y - c.y).powi(2) <= (c.radius * factor).powi(2)
                };
                let role = if self.inventory {
                    if hit(&controls[4], 1.25) {
                        14
                    } else {
                        return Ok((false, actions));
                    }
                } else {
                    controls
                        .iter()
                        .find(|c| c.id != MOVE_CONTROL_ID && hit(c, 1.25))
                        .map(|c| c.id)
                        .unwrap_or_else(|| {
                            let extent = movement.radius * MOVE_HALF_EXTENT;
                            if (x - movement.x).abs() <= extent
                                && (y - movement.y).abs() <= extent
                                && !self.fingers.values().any(|f| f.role == MOVE_CONTROL_ID)
                            {
                                MOVE_CONTROL_ID
                            } else {
                                20
                            }
                        })
                };
                let held = matches!(role, 5 | 6 | 7 | 10 | 11 | DEBUG_OVERLAY_ID)
                    && self.fingers.values().any(|f| f.role == role);
                self.fingers.insert(id, Finger { role, x, y });
                if role == MOVE_CONTROL_ID {
                    let [side, forward] = movement_axis(movement, x, y);
                    emit(1, side);
                    emit(2, forward);
                } else if (role <= 14 || role == DEBUG_OVERLAY_ID) && !held {
                    emit(role, 1.0);
                }
            }
            1 => {
                let Some(f) = self.fingers.get_mut(&id) else {
                    return Ok((false, actions));
                };
                if f.role == MOVE_CONTROL_ID {
                    let [side, forward] = movement_axis(movement, x, y);
                    emit(1, side);
                    emit(2, forward);
                } else if f.role == 20 {
                    emit(3, (x - f.x) * 1200.0 / w.min(h) * self.touch.sensitivity);
                    emit(4, (y - f.y) * 1200.0 / w.min(h) * self.touch.sensitivity);
                }
                f.x = x;
                f.y = y;
            }
            _ => {
                let Some(f) = self.fingers.remove(&id) else {
                    return Ok((false, actions));
                };
                if f.role == MOVE_CONTROL_ID {
                    emit(1, 0.0);
                    emit(2, 0.0);
                } else if matches!(f.role, 5 | 6 | 7 | 10 | 11)
                    && !self.fingers.values().any(|other| other.role == f.role)
                {
                    emit(f.role, 0.0);
                }
            }
        }
        if actions
            .iter()
            .any(|a| !a.value.is_finite() || a.value.abs() > 4096.0)
        {
            return Err("invalid touch action".into());
        }
        Ok((true, actions))
    }
}

fn movement_axis(control: &Control, x: f32, y: f32) -> [f32; 2] {
    let size = control.radius * MOVE_HALF_EXTENT;
    let side = (control.x - x) / size;
    let forward = (control.y - y) / size;
    let magnitude = (side * side + forward * forward).sqrt().max(1.0);
    [side / magnitude, forward / magnitude]
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn simultaneous_touches_hold_until_last_release_and_menu_back_claims_only_its_button() {
        let mut controls = TouchModule::new();
        let jump = controls.geometry(1000.0, 600.0)[0].clone();
        assert_eq!(
            controls
                .touch(1, 0, jump.x, jump.y, 1000.0, 600.0)
                .unwrap()
                .1,
            vec![Action { id: 5, value: 1.0 }]
        );
        assert!(
            controls
                .touch(2, 0, jump.x, jump.y, 1000.0, 600.0)
                .unwrap()
                .1
                .is_empty()
        );
        assert!(
            controls
                .touch(1, 2, jump.x, jump.y, 1000.0, 600.0)
                .unwrap()
                .1
                .is_empty()
        );
        assert_eq!(
            controls
                .touch(2, 3, jump.x, jump.y, 1000.0, 600.0)
                .unwrap()
                .1,
            vec![Action { id: 5, value: 0.0 }]
        );
        controls.set_context(true).unwrap();
        assert!(!controls.touch(3, 0, 500.0, 300.0, 1000.0, 600.0).unwrap().0);
        let back = controls.draw(1000.0, 600.0).unwrap().remove(0);
        assert_eq!(
            controls
                .touch(4, 0, back.x, back.y, 1000.0, 600.0)
                .unwrap()
                .1,
            vec![Action { id: 14, value: 1.0 }]
        );
    }
    #[test]
    fn debug_overlay_claims_one_press_and_stays_out_of_inventory() {
        let mut controls = TouchModule::new();
        let (width, height) = (1000.0, 600.0);
        let debug = controls
            .draw(width, height)
            .unwrap()
            .into_iter()
            .find(|control| control.id == DEBUG_OVERLAY_ID)
            .unwrap();
        let tap = |controls: &mut TouchModule, id, phase| {
            controls
                .touch(id, phase, debug.x, debug.y, width, height)
                .unwrap()
        };
        assert_eq!(
            tap(&mut controls, 1, 0),
            (true, vec![Action { id: DEBUG_OVERLAY_ID, value: 1.0 }])
        );
        assert_eq!(tap(&mut controls, 2, 0), (true, vec![]));
        assert_eq!(tap(&mut controls, 1, 1), (true, vec![]));
        assert_eq!(tap(&mut controls, 1, 2), (true, vec![]));
        assert_eq!(tap(&mut controls, 2, 3), (true, vec![]));

        controls.set_context(true).unwrap();
        assert_eq!(tap(&mut controls, 3, 0), (false, vec![]));
    }

    #[test]
    fn movement_starts_on_press_at_the_drawn_corner_and_stops_on_release() {
        let mut controls = TouchModule::new();
        let (width, height) = (1000.0, 600.0);
        let movement = controls.draw(width, height).unwrap().into_iter()
            .find(|control| control.id == MOVE_CONTROL_ID).unwrap();
        let inset = movement.radius * MOVE_HALF_EXTENT * 0.99;
        let (_, actions) = controls
            .touch(1, 0, movement.x - inset, movement.y - inset, width, height)
            .unwrap();
        assert_eq!(actions.len(), 2);
        assert_eq!((actions[0].id, actions[1].id), (1, 2));
        assert!(actions[0].value > 0.7 && actions[1].value > 0.7);
        let (_, actions) = controls
            .touch(1, 1, movement.x + inset, movement.y, width, height).unwrap();
        assert!(actions[0].value < -0.98);
        assert_eq!(actions[1].value, 0.0);
        let (_, actions) = controls
            .touch(1, 2, movement.x + inset, movement.y, width, height).unwrap();
        assert_eq!(actions, [Action { id: 1, value: 0.0 }, Action { id: 2, value: 0.0 }]);
    }

    #[test]
    fn gyro_axes_and_settings_match_screen_rotation() {
        assert_eq!(gyro_turn([1.0, 0.0], 0), [0.0, -1f32.to_degrees()]);
        assert_eq!(gyro_turn([1.0, 0.0], 1), [-1f32.to_degrees(), -0.0]);
        assert_eq!(
            GyroSettings::parse("gyro_enabled=true\ngyro_sensitivity=9"),
            Some(GyroSettings {
                enabled: true,
                sensitivity: 3.0
            })
        );
        assert!(TouchSettings::parse("control_scale=NaN").is_none());
    }
}
