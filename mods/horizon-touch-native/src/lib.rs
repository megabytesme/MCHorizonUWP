//! Optional build-time touch controls for the UWP game host.
//! This native source mod owns the control layout and touch state; it does not
//! load code at runtime or depend on the game renderer or windowing APIs.
#![forbid(unsafe_code)]
/// Stable ID shared with the game mod list and builder.
pub const MOD_ID: &str = "horizon_touch";
pub const MOD_TITLE: &str = "Touch Controls 0.1.0";
pub const MOD_DESCRIPTION: &str = "Touchscreen controls built into this game.";

const MOVE_X: i32 = 1;
const MOVE_Y: i32 = 2;
const LOOK_X: i32 = 3;
const LOOK_Y: i32 = 4;
const JUMP: i32 = 5;
const ATTACK: i32 = 6;
const USE: i32 = 7;
const INVENTORY: i32 = 8;
const PAUSE: i32 = 9;
const SPRINT: i32 = 10;
const SNEAK: i32 = 11;
const PREV: i32 = 12;
const NEXT: i32 = 13;
const BACK: i32 = 14;
const LOOK: i32 = 20;
const STICK: i32 = 21;
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
impl GyroSettings {
    pub fn parse(text: &str) -> Option<Self> {
        let mut settings = Self::default();
        for line in text.lines() {
            let line = line.trim();
            if line.is_empty() {
                continue;
            }
            let Some((key, value)) = line.split_once('=') else {
                continue;
            };
            match key.trim() {
                "enabled" | "gyro_enabled" => {
                    settings.enabled = matches!(value.trim(), "1" | "true")
                }
                "sensitivity" | "gyro_sensitivity" => {
                    let value = value.trim().parse::<f32>().ok()?;
                    if !value.is_finite() {
                        return None;
                    }
                    settings.sensitivity = value.clamp(0.25, 3.0);
                }
                _ => {}
            }
        }
        Some(settings)
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

impl TouchSettings {
    pub fn parse(text: &str) -> Option<Self> {
        let mut settings = Self::default();
        for line in text.lines() {
            let line = line.trim();
            let Some((key, value)) = line.split_once('=') else {
                continue;
            };
            let key = key.trim();
            if !matches!(key, "touch_sensitivity" | "control_scale") {
                continue;
            }
            let value = value.trim().parse::<f32>().ok()?;
            if !value.is_finite() {
                return None;
            }
            match key {
                "touch_sensitivity" => settings.sensitivity = value.clamp(0.25, 3.0),
                "control_scale" => settings.control_scale = value.clamp(0.6, 1.5),
                _ => unreachable!(),
            }
        }
        Some(settings)
    }
}

/// Convert angular movement from device-native axes into yaw and pitch deltas.
/// The axis rotation follows Starling's MIT-licensed touch implementation.
pub fn gyro_turn(turned: [f32; 2], rotation: i32) -> [f32; 2] {
    let [x, y] = turned;
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
    id: i32,
    role: i32,
    x: f32,
    y: f32,
}
const EMPTY: Finger = Finger {
    id: -1,
    role: 0,
    x: 0.,
    y: 0.,
};
fn geometry(w: f32, h: f32, scale: f32) -> [(i32, f32, f32); 10] {
    let s = w.min(h);
    [
        (JUMP, w - s * 0.11 * scale, h - s * 0.14 * scale),
        (ATTACK, w - s * 0.27 * scale, h - s * 0.25 * scale),
        (USE, w - s * 0.11 * scale, h - s * 0.36 * scale),
        (INVENTORY, w - s * 0.08 * scale, s * 0.08 * scale),
        (PAUSE, s * 0.08 * scale, s * 0.08 * scale),
        (SPRINT, s * 0.09 * scale, h - s * 0.43 * scale),
        (SNEAK, s * 0.26 * scale, h - s * 0.43 * scale),
        (PREV, w * 0.5 - s * 0.20 * scale, h - s * 0.075 * scale),
        (NEXT, w * 0.5 + s * 0.20 * scale, h - s * 0.075 * scale),
        (STICK, s * 0.18 * scale, h - s * 0.18 * scale),
    ]
}
fn action(actions: &mut Vec<Action>, id: i32, value: f32) {
    actions.push(Action { id, value });
}
pub struct TouchModule {
    fingers: [Finger; 16],
    inventory: bool,
    gyro: GyroSettings,
    touch: TouchSettings,
}
impl Default for TouchModule {
    fn default() -> Self {
        Self {
            fingers: [EMPTY; 16],
            inventory: false,
            gyro: GyroSettings::default(),
            touch: TouchSettings::default(),
        }
    }
}
impl TouchModule {
    pub fn new() -> Self {
        Self::default()
    }
    pub fn supports_menu(&self) -> bool {
        true
    }
    pub fn set_gyro_settings(&mut self, settings: GyroSettings) {
        self.gyro = GyroSettings {
            enabled: settings.enabled,
            sensitivity: if settings.sensitivity.is_finite() {
                settings.sensitivity.clamp(0.25, 3.0)
            } else {
                1.0
            },
        };
    }
    pub fn gyro_settings(&self) -> GyroSettings {
        self.gyro
    }
    pub fn set_touch_settings(&mut self, settings: TouchSettings) {
        self.touch = TouchSettings {
            sensitivity: if settings.sensitivity.is_finite() {
                settings.sensitivity.clamp(0.25, 3.0)
            } else {
                1.0
            },
            control_scale: if settings.control_scale.is_finite() {
                settings.control_scale.clamp(0.6, 1.5)
            } else {
                1.0
            },
        };
    }
    pub fn touch_settings(&self) -> TouchSettings {
        self.touch
    }
    pub fn set_context(&mut self, inventory: bool) -> Result<(), String> {
        self.inventory = inventory;
        Ok(())
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
            || w <= 0.
            || h <= 0.
        {
            return Err("invalid input".into());
        }
        let mut actions = Vec::new();
        let s = w.min(h);
        let radius = s * 0.075 * self.touch.control_scale;
        let positions = geometry(w, h, self.touch.control_scale);
        let existing = self.fingers.iter().position(|f| f.id == id);
        let claimed = match phase {
            0 => {
                if existing.is_some() {
                    return Ok((false, actions));
                }
                if self.inventory {
                    let (bx, by) = (
                        s * 0.08 * self.touch.control_scale,
                        s * 0.08 * self.touch.control_scale,
                    );
                    if (x - bx).powi(2) + (y - by).powi(2) > (radius * 1.25).powi(2) {
                        return Ok((false, actions));
                    }
                    let Some(slot) = self.fingers.iter().position(|f| f.id < 0) else {
                        return Ok((false, actions));
                    };
                    self.fingers[slot] = Finger {
                        id,
                        role: BACK,
                        x,
                        y,
                    };
                    action(&mut actions, BACK, 1.);
                    true
                } else {
                    let Some(slot) = self.fingers.iter().position(|f| f.id < 0) else {
                        return Ok((false, actions));
                    };
                    let mut role = LOOK;
                    for &(button, bx, by) in &positions[..9] {
                        let (dx, dy) = (x - bx, y - by);
                        if dx * dx + dy * dy <= (radius * 1.25).powi(2) {
                            role = button;
                            break;
                        }
                    }
                    let (_, sx, sy) = positions[9];
                    let (dx, dy) = (x - sx, y - sy);
                    if role == LOOK
                        && dx * dx + dy * dy <= (radius * 2.2).powi(2)
                        && !self.fingers.iter().any(|f| f.role == STICK)
                    {
                        role = STICK;
                    }
                    let held = matches!(role, JUMP | ATTACK | USE | SPRINT | SNEAK)
                        && self.fingers.iter().any(|f| f.id >= 0 && f.role == role);
                    self.fingers[slot] = Finger { id, role, x, y };
                    if role <= NEXT && !held {
                        action(&mut actions, role, 1.);
                    }
                    true
                }
            }
            1 => {
                let Some(slot) = existing else {
                    return Ok((false, actions));
                };
                let f = self.fingers[slot];
                if f.role == STICK {
                    let (_, sx, sy) = positions[9];
                    let mut vx = (x - sx) / (radius * 1.8);
                    let mut vy = (sy - y) / (radius * 1.8);
                    let length = (vx * vx + vy * vy).sqrt();
                    if length > 1. {
                        vx /= length;
                        vy /= length;
                    }
                    action(&mut actions, MOVE_X, -vx);
                    action(&mut actions, MOVE_Y, vy);
                } else if f.role == LOOK {
                    action(
                        &mut actions,
                        LOOK_X,
                        (x - f.x) * 1200. / s * self.touch.sensitivity,
                    );
                    action(
                        &mut actions,
                        LOOK_Y,
                        (y - f.y) * 1200. / s * self.touch.sensitivity,
                    );
                }
                self.fingers[slot].x = x;
                self.fingers[slot].y = y;
                true
            }
            2 | 3 => {
                let Some(slot) = existing else {
                    return Ok((false, actions));
                };
                let role = self.fingers[slot].role;
                self.fingers[slot] = EMPTY;
                if role == STICK {
                    action(&mut actions, MOVE_X, 0.);
                    action(&mut actions, MOVE_Y, 0.);
                } else if matches!(role, JUMP | ATTACK | USE | SPRINT | SNEAK)
                    && !self.fingers.iter().any(|f| f.id >= 0 && f.role == role)
                {
                    action(&mut actions, role, 0.);
                }
                true
            }
            _ => unreachable!(),
        };
        if actions
            .iter()
            .any(|a| !a.value.is_finite() || a.value.abs() > 4096.0)
        {
            return Err("invalid touch action".into());
        }
        Ok((claimed, actions))
    }
    pub fn draw(&mut self, w: f32, h: f32) -> Result<Vec<Control>, String> {
        if !w.is_finite() || !h.is_finite() || w <= 0. || h <= 0. {
            return Err("invalid viewport".into());
        }
        let radius = w.min(h) * 0.075 * self.touch.control_scale;
        let positions = geometry(w, h, self.touch.control_scale);
        let controls: Vec<Control> = if self.inventory {
            vec![Control {
                id: BACK,
                x: w.min(h) * 0.08 * self.touch.control_scale,
                y: w.min(h) * 0.08 * self.touch.control_scale,
                radius,
            }]
        } else {
            positions
                .into_iter()
                .map(|(id, x, y)| Control { id, x, y, radius })
                .collect()
        };
        if controls.iter().any(|c| {
            ![c.x, c.y, c.radius]
                .iter()
                .all(|v| v.is_finite() && v.abs() <= 32768.0)
                || c.radius <= 0.0
        }) {
            return Err("invalid touch control".into());
        }
        Ok(controls)
    }
}
