//! Multitouch controls for the UWP CoreWindow backend.
use horizon_client::hud::{HudAtlas, HudBatch, rgba};
#[cfg(all(
    any(target_vendor = "uwp", test),
    feature = "native-d3d11",
    feature = "native-touch-mod"
))]
use horizon_touch_native as touch_module;
use std::collections::HashMap;
use winit::event::{Touch, TouchPhase, WindowEvent};

#[cfg(all(target_vendor = "uwp", feature = "native-touch-mod"))]
pub(super) type GyroEnable = fn(bool) -> bool;
#[cfg(all(target_vendor = "uwp", feature = "native-touch-mod"))]
pub(super) type GyroTake = fn() -> Option<(f32, f32, i32)>;
#[cfg(all(target_vendor = "uwp", feature = "native-touch-mod"))]
static GYRO_ADAPTER: std::sync::Mutex<Option<(GyroEnable, GyroTake)>> = std::sync::Mutex::new(None);

#[cfg(all(target_vendor = "uwp", feature = "native-touch-mod"))]
pub(super) fn install_gyro_adapter(enable: GyroEnable, take: GyroTake) {
    *GYRO_ADAPTER
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner()) = Some((enable, take));
}

#[cfg(all(target_vendor = "uwp", feature = "native-touch-mod"))]
fn gyro_adapter() -> Option<(GyroEnable, GyroTake)> {
    *GYRO_ADAPTER
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Button {
    Jump,
    Attack,
    Use,
    Inventory,
    Pause,
    Sprint,
    Sneak,
    Previous,
    Next,
}
#[derive(Clone, Copy, Debug)]
enum Role {
    Menu,
    Move,
    Look([f32; 2]),
    Button(Button),
}
#[derive(Debug, PartialEq)]
enum Action {
    Jump(bool),
    Attack(bool),
    Use(bool),
    Inventory,
    Pause,
    Sprint(bool),
    Sneak(bool),
    Hotbar(bool),
    Look(f32, f32),
    Back,
}

pub(super) struct TouchControls {
    contacts: HashMap<u64, Role>,
    primary: Option<u64>,
    primary_claimed: bool,
    suppressed: u8,
    pub(super) movement: Option<[f32; 2]>,
    used: bool,
    visible: bool,
    compat_touch: Option<u64>,
    compat_mouse_events: u8,
    #[cfg(all(
        any(target_vendor = "uwp", test),
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    module: Option<touch_module::TouchModule>,
    #[cfg(all(
        any(target_vendor = "uwp", test),
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    mod_enabled: bool,
    #[cfg(all(
        any(target_vendor = "uwp", test),
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    release_after_failure: bool,
    #[cfg(all(
        any(target_vendor = "uwp", test),
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    gyro_requested: bool,
    #[cfg(all(
        any(target_vendor = "uwp", test),
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    gyro_active: bool,
    #[cfg(all(any(target_vendor = "uwp", test), feature = "native-d3d11"))]
    pub(super) inventory_visible: bool,
}

impl Default for TouchControls {
    fn default() -> Self {
        #[cfg(all(
            any(target_vendor = "uwp", test),
            feature = "native-d3d11",
            feature = "native-touch-mod"
        ))]
        let module = Some(create_touch_module());
        Self {
            contacts: HashMap::new(),
            primary: None,
            primary_claimed: false,
            suppressed: 0,
            movement: None,
            used: false,
            visible: cfg!(target_arch = "arm"),
            compat_touch: None,
            compat_mouse_events: 0,
            #[cfg(all(
                any(target_vendor = "uwp", test),
                feature = "native-d3d11",
                feature = "native-touch-mod"
            ))]
            module,
            #[cfg(all(
                any(target_vendor = "uwp", test),
                feature = "native-d3d11",
                feature = "native-touch-mod"
            ))]
            mod_enabled: true,
            #[cfg(all(
                any(target_vendor = "uwp", test),
                feature = "native-d3d11",
                feature = "native-touch-mod"
            ))]
            release_after_failure: false,
            #[cfg(all(
                any(target_vendor = "uwp", test),
                feature = "native-d3d11",
                feature = "native-touch-mod"
            ))]
            gyro_requested: false,
            #[cfg(all(
                any(target_vendor = "uwp", test),
                feature = "native-d3d11",
                feature = "native-touch-mod"
            ))]
            gyro_active: false,
            #[cfg(all(any(target_vendor = "uwp", test), feature = "native-d3d11"))]
            inventory_visible: false,
        }
    }
}

#[cfg(all(target_vendor = "uwp", feature = "native-touch-mod"))]
fn create_touch_module() -> touch_module::TouchModule {
    let mut module = touch_module::TouchModule::new();
    let home = horizon_client::logging::game_home();
    let path = home.join("touch-settings.txt");
    let saved = std::fs::read_to_string(path)
        .or_else(|_| std::fs::read_to_string(home.join("touch-gyro-settings.txt")));
    if let Ok(saved) = saved {
        if let Some(settings) = touch_module::GyroSettings::parse(&saved) {
            module.set_gyro_settings(settings);
        }
        if let Some(settings) = touch_module::TouchSettings::parse(&saved) {
            module.set_touch_settings(settings);
        }
    }
    module
}

#[cfg(all(not(target_vendor = "uwp"), test, feature = "native-touch-mod"))]
fn create_touch_module() -> touch_module::TouchModule {
    touch_module::TouchModule::new()
}

fn layout(size: [f32; 2]) -> (f32, [(Button, [f32; 2]); 9], [f32; 2]) {
    let s = size[0].min(size[1]);
    (
        s * 0.075,
        [
            (Button::Jump, [size[0] - s * 0.11, size[1] - s * 0.14]),
            (Button::Attack, [size[0] - s * 0.27, size[1] - s * 0.25]),
            (Button::Use, [size[0] - s * 0.11, size[1] - s * 0.36]),
            (Button::Inventory, [size[0] - s * 0.08, s * 0.08]),
            (Button::Pause, [s * 0.08, s * 0.08]),
            (Button::Sprint, [s * 0.09, size[1] - s * 0.43]),
            (Button::Sneak, [s * 0.26, size[1] - s * 0.43]),
            (
                Button::Previous,
                [size[0] * 0.5 - s * 0.20, size[1] - s * 0.075],
            ),
            (
                Button::Next,
                [size[0] * 0.5 + s * 0.20, size[1] - s * 0.075],
            ),
        ],
        [s * 0.18, size[1] - s * 0.18],
    )
}
fn distance(a: [f32; 2], b: [f32; 2]) -> f32 {
    ((a[0] - b[0]).powi(2) + (a[1] - b[1]).powi(2)).sqrt()
}

impl TouchControls {
    /// UWP follows a touch with one or two mouse-compatible events for menus.
    /// Keep those usable without treating them as a switch to a real mouse.
    pub(super) fn note_touch(&mut self, id: u64, phase: TouchPhase) {
        if phase == TouchPhase::Started {
            self.visible = true;
            if self.compat_touch.is_none() {
                self.compat_touch = Some(id);
            }
        }
        if self.compat_touch == Some(id) {
            self.compat_mouse_events = if phase == TouchPhase::Moved { 1 } else { 2 };
            if matches!(phase, TouchPhase::Ended | TouchPhase::Cancelled) {
                self.compat_touch = None;
            }
        } else {
            self.compat_mouse_events = 0;
        }
    }

    pub(super) fn touch_compatible_mouse(&mut self, event: &WindowEvent) -> bool {
        if self.compat_mouse_events > 0
            && matches!(
                event,
                WindowEvent::CursorMoved { .. } | WindowEvent::MouseInput { .. }
            )
        {
            self.compat_mouse_events -= 1;
            true
        } else {
            self.compat_mouse_events = 0;
            false
        }
    }

    pub(super) fn hide_for_other_input(&mut self) {
        self.visible = false;
    }
    /// On disable, release any held gameplay inputs before resetting touch state.
    #[cfg(all(
        any(target_vendor = "uwp", test),
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    fn set_mod_enabled(&mut self, enabled: bool) -> Vec<Action> {
        if self.mod_enabled == enabled {
            return Vec::new();
        }
        self.mod_enabled = enabled;
        self.contacts.clear();
        self.primary = None;
        self.primary_claimed = false;
        self.suppressed = 0;
        self.movement = None;
        self.release_after_failure = false;
        self.gyro_active = false;
        self.gyro_requested = false;
        self.module = enabled.then(create_touch_module);
        if enabled {
            Vec::new()
        } else {
            vec![
                Action::Jump(false),
                Action::Attack(false),
                Action::Use(false),
                Action::Sprint(false),
                Action::Sneak(false),
            ]
        }
    }

    fn update(
        &mut self,
        id: u64,
        phase: TouchPhase,
        p: [f32; 2],
        size: [f32; 2],
        playing: bool,
    ) -> Vec<Action> {
        #[cfg(all(
            any(target_vendor = "uwp", test),
            feature = "native-d3d11",
            feature = "native-touch-mod"
        ))]
        if !self.mod_enabled {
            return Vec::new();
        }
        #[cfg(all(
            any(target_vendor = "uwp", test),
            feature = "native-d3d11",
            feature = "native-touch-mod"
        ))]
        if self.module.as_ref().is_some_and(|m| {
            playing
                || (self.inventory_visible && m.supports_menu())
                || self.contacts.contains_key(&id)
        }) {
            if let Ok(id32) = i32::try_from(id) {
                let phase32 = match phase {
                    TouchPhase::Started => 0,
                    TouchPhase::Moved => 1,
                    TouchPhase::Ended => 2,
                    TouchPhase::Cancelled => 3,
                };
                let module = self.module.as_mut().unwrap();
                let result = module
                    .set_context(self.inventory_visible)
                    .and_then(|_| module.touch(id32, phase32, p[0], p[1], size[0], size[1]));
                match result {
                    Ok((true, mod_actions)) => {
                        self.used = true;
                        match phase {
                            TouchPhase::Started => {
                                if self.contacts.is_empty() {
                                    self.primary = Some(id);
                                    self.primary_claimed = true;
                                }
                                self.contacts.insert(id, Role::Menu);
                            }
                            TouchPhase::Ended | TouchPhase::Cancelled => {
                                self.contacts.remove(&id);
                            }
                            TouchPhase::Moved => {}
                        }
                        if self.primary == Some(id) {
                            self.suppressed = if phase == TouchPhase::Moved { 1 } else { 2 };
                        }
                        if matches!(phase, TouchPhase::Ended | TouchPhase::Cancelled)
                            && self.primary == Some(id)
                        {
                            self.primary = None;
                            self.primary_claimed = false;
                        }
                        return self.translate_mod_actions(mod_actions);
                    }
                    Ok((false, _)) => {}
                    Err(error) => {
                        log::warn!(
                            "Horizon: touch module failed ({error}); using built-in controls"
                        );
                        self.module = None;
                        self.contacts.clear();
                        self.primary = None;
                        self.primary_claimed = false;
                        self.movement = None;
                        self.release_after_failure = true;
                    }
                }
            }
        }
        let mut actions = self.update_builtin(id, phase, p, size, playing);
        #[cfg(all(
            any(target_vendor = "uwp", test),
            feature = "native-d3d11",
            feature = "native-touch-mod"
        ))]
        actions.splice(0..0, self.take_failure_actions());
        actions
    }

    fn update_builtin(
        &mut self,
        id: u64,
        phase: TouchPhase,
        p: [f32; 2],
        size: [f32; 2],
        playing: bool,
    ) -> Vec<Action> {
        let mut result = Vec::new();
        let (radius, buttons, stick) = layout(size);
        match phase {
            TouchPhase::Started => {
                self.used = true;
                if self.contacts.is_empty() {
                    self.primary = Some(id);
                    self.primary_claimed = playing;
                }
                let role = if !playing {
                    Role::Menu
                } else if let Some((button, _)) = buttons
                    .iter()
                    .find(|(_, c)| distance(*c, p) <= radius * 1.25)
                {
                    Role::Button(*button)
                } else if distance(stick, p) <= radius * 2.2
                    && !self.contacts.values().any(|r| matches!(r, Role::Move))
                {
                    Role::Move
                } else {
                    Role::Look(p)
                };
                if self.primary == Some(id) && self.primary_claimed {
                    self.suppressed = 2;
                }
                match role {
                    Role::Button(Button::Jump) => result.push(Action::Jump(true)),
                    Role::Button(Button::Attack) => result.push(Action::Attack(true)),
                    Role::Button(Button::Use) => result.push(Action::Use(true)),
                    Role::Button(Button::Inventory) => result.push(Action::Inventory),
                    Role::Button(Button::Pause) => result.push(Action::Pause),
                    Role::Button(Button::Sprint) => result.push(Action::Sprint(true)),
                    Role::Button(Button::Sneak) => result.push(Action::Sneak(true)),
                    Role::Button(Button::Previous) => result.push(Action::Hotbar(false)),
                    Role::Button(Button::Next) => result.push(Action::Hotbar(true)),
                    Role::Move => self.movement = Some([0., 0.]),
                    _ => {}
                }
                self.contacts.insert(id, role);
            }
            TouchPhase::Moved => {
                if self.primary == Some(id) && self.primary_claimed {
                    self.suppressed = 1;
                }
                if let Some(role) = self.contacts.get_mut(&id) {
                    match role {
                        Role::Move => {
                            let mut x = (p[0] - stick[0]) / (radius * 1.8);
                            let mut y = (stick[1] - p[1]) / (radius * 1.8);
                            let length = (x * x + y * y).sqrt();
                            if length > 1. {
                                x /= length;
                                y /= length;
                            }
                            // Game movement uses positive left, while screen X grows right.
                            self.movement = Some([-x, y]);
                        }
                        Role::Look(old) if playing => {
                            let s = size[0].min(size[1]).max(1.);
                            result.push(Action::Look(
                                (p[0] - old[0]) * 1200. / s,
                                (p[1] - old[1]) * 1200. / s,
                            ));
                            *old = p;
                        }
                        _ => {}
                    }
                }
            }
            TouchPhase::Ended | TouchPhase::Cancelled => {
                if self.primary == Some(id) && self.primary_claimed {
                    self.suppressed = 2;
                }
                match self.contacts.remove(&id) {
                    Some(Role::Move) => self.movement = None,
                    Some(Role::Button(Button::Jump)) => result.push(Action::Jump(false)),
                    Some(Role::Button(Button::Attack)) => result.push(Action::Attack(false)),
                    Some(Role::Button(Button::Use)) => result.push(Action::Use(false)),
                    Some(Role::Button(Button::Sprint)) => result.push(Action::Sprint(false)),
                    Some(Role::Button(Button::Sneak)) => result.push(Action::Sneak(false)),
                    _ => {}
                }
                if self.primary == Some(id) {
                    self.primary = None;
                    self.primary_claimed = false;
                }
            }
        }
        result
    }
    pub(super) fn suppress_mouse(&mut self, event: &WindowEvent) -> bool {
        if self.suppressed == 0 {
            return false;
        }
        if matches!(
            event,
            WindowEvent::CursorMoved { .. } | WindowEvent::MouseInput { .. }
        ) {
            self.suppressed -= 1;
            true
        } else {
            self.suppressed = 0;
            false
        }
    }
    #[cfg(all(
        any(target_vendor = "uwp", test),
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    fn translate_mod_actions(&mut self, actions: Vec<touch_module::Action>) -> Vec<Action> {
        let mut output = Vec::new();
        for a in actions {
            let down = a.value > 0.5;
            match a.id {
                1 | 2 => {
                    let mut v = self.movement.unwrap_or([0., 0.]);
                    v[(a.id - 1) as usize] = a.value.clamp(-1., 1.);
                    self.movement = if v == [0., 0.] { None } else { Some(v) };
                }
                3 => output.push(Action::Look(a.value, 0.)),
                4 => output.push(Action::Look(0., a.value)),
                5 => output.push(Action::Jump(down)),
                6 => output.push(Action::Attack(down)),
                7 => output.push(Action::Use(down)),
                8 if down => output.push(Action::Inventory),
                9 if down => output.push(Action::Pause),
                10 => output.push(Action::Sprint(down)),
                11 => output.push(Action::Sneak(down)),
                12 if down => output.push(Action::Hotbar(false)),
                13 if down => output.push(Action::Hotbar(true)),
                14 if down => output.push(Action::Back),
                _ => {}
            }
        }
        output
    }
    #[cfg(all(
        any(target_vendor = "uwp", test),
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    fn take_failure_actions(&mut self) -> Vec<Action> {
        if std::mem::take(&mut self.release_after_failure) {
            self.movement = None;
            vec![
                Action::Jump(false),
                Action::Attack(false),
                Action::Use(false),
                Action::Sprint(false),
                Action::Sneak(false),
            ]
        } else {
            Vec::new()
        }
    }
    pub(super) fn draw(&mut self, hud: &mut HudBatch, atlas: &HudAtlas) {
        if !self.visible {
            return;
        }
        #[cfg(all(
            any(target_vendor = "uwp", test),
            feature = "native-d3d11",
            feature = "native-touch-mod"
        ))]
        if !self.mod_enabled {
            return;
        }
        if !self.used && !cfg!(target_arch = "arm") {
            return;
        }
        let size = [hud.width, hud.height];
        #[cfg(all(
            any(target_vendor = "uwp", test),
            feature = "native-d3d11",
            feature = "native-touch-mod"
        ))]
        if let Some(module) = &mut self.module {
            if self.inventory_visible && !module.supports_menu() {
                return;
            }
            match module
                .set_context(self.inventory_visible)
                .and_then(|_| module.draw(size[0], size[1]))
            {
                Ok(controls) => {
                    let bg = rgba(25, 25, 25, 115);
                    let fg = rgba(255, 255, 255, 220);
                    for c in controls {
                        let label = match c.id {
                            5 => "JUMP",
                            6 => "HIT",
                            7 => "USE",
                            8 => "INV",
                            9 => "II",
                            10 => "SPR",
                            11 => "SNEAK",
                            12 => "<",
                            13 => ">",
                            14 => "BACK",
                            21 => "MOVE",
                            _ => continue,
                        };
                        let scale = if c.id == 21 { 1.7 } else { 1. };
                        hud.fill(
                            atlas,
                            c.x - c.radius * scale,
                            c.y - c.radius * scale,
                            c.radius * 2. * scale,
                            c.radius * 2. * scale,
                            bg,
                        );
                        hud.text(atlas, label, c.x - c.radius * 0.65, c.y - 4., fg, false);
                    }
                    return;
                }
                Err(error) => {
                    log::warn!(
                        "Horizon: touch module draw failed ({error}); using built-in controls"
                    );
                    self.module = None;
                    self.contacts.clear();
                    self.primary = None;
                    self.primary_claimed = false;
                    self.movement = None;
                    self.release_after_failure = true;
                }
            }
        }
        #[cfg(all(any(target_vendor = "uwp", test), feature = "native-d3d11"))]
        if self.inventory_visible {
            return;
        }
        let (r, buttons, stick) = layout(size);
        let bg = rgba(25, 25, 25, 115);
        let fg = rgba(255, 255, 255, 220);
        hud.fill(
            atlas,
            stick[0] - r * 1.7,
            stick[1] - r * 1.7,
            r * 3.4,
            r * 3.4,
            bg,
        );
        hud.text(atlas, "MOVE", stick[0] - r, stick[1] - 4., fg, false);
        for (b, p) in buttons {
            hud.fill(atlas, p[0] - r, p[1] - r, r * 2., r * 2., bg);
            let label = match b {
                Button::Jump => "JUMP",
                Button::Attack => "HIT",
                Button::Use => "USE",
                Button::Inventory => "INV",
                Button::Pause => "II",
                Button::Sprint => "SPR",
                Button::Sneak => "SNEAK",
                Button::Previous => "<",
                Button::Next => ">",
            };
            hud.text(atlas, label, p[0] - r * 0.65, p[1] - 4., fg, false);
        }
    }
}

#[cfg(target_vendor = "uwp")]
impl super::Game<'_> {
    #[cfg(feature = "native-touch-mod")]
    pub(super) fn touch_gyro_frame(&mut self) {
        let settings = self
            .touch
            .module
            .as_ref()
            .map(touch_module::TouchModule::gyro_settings)
            .unwrap_or_default();
        let wanted = self.touch.mod_enabled
            && settings.enabled
            && self.title.is_none()
            && self.screen == super::Screen::Playing
            && self.player.is_some();

        if wanted != self.touch.gyro_requested {
            self.touch.gyro_requested = wanted;
            // A missing or disabled device sensor simply leaves touch look available.
            self.touch.gyro_active =
                gyro_adapter().is_some_and(|(enable, _)| enable(wanted) && wanted);
            if wanted && !self.touch.gyro_active {
                log::info!("Horizon: no gyrometer is available for touch aiming");
            }
        }
        if !self.touch.gyro_active {
            return;
        }

        let Some((x, y, rotation)) = gyro_adapter().and_then(|(_, take)| take()) else {
            return;
        };
        if !x.is_finite() || !y.is_finite() {
            return;
        }
        let [yaw, pitch] = touch_module::gyro_turn([x, y], rotation);
        if !yaw.is_finite() || !pitch.is_finite() {
            return;
        }
        let sensitivity = settings.sensitivity.clamp(0.25, 3.0);
        self.turn_player(
            f64::from(yaw.clamp(-45.0, 45.0) / 0.15 * sensitivity),
            f64::from(pitch.clamp(-45.0, 45.0) / 0.15 * sensitivity),
        );
    }

    pub(super) fn touch_event(&mut self, touch: Touch) {
        if touch.phase == TouchPhase::Started {
            self.pad_other_input();
        }
        self.touch.note_touch(touch.id, touch.phase);
        #[cfg(all(feature = "native-d3d11", feature = "native-touch-mod"))]
        self.sync_touch_mod();
        let Some(size) = self.renderer_viewport() else {
            return;
        };
        let playing =
            self.title.is_none() && self.screen == super::Screen::Playing && self.player.is_some();
        #[cfg(feature = "native-d3d11")]
        {
            self.touch.inventory_visible = matches!(
                self.screen,
                super::Screen::Inventory | super::Screen::Creative
            );
        }
        let actions = self.touch.update(
            touch.id,
            touch.phase,
            [touch.location.x as f32, touch.location.y as f32],
            [size[0] as f32, size[1] as f32],
            playing,
        );
        self.apply_touch_actions(actions);
    }

    #[cfg(all(feature = "native-d3d11", feature = "native-touch-mod"))]
    pub(super) fn sync_touch_mod(&mut self) {
        let enabled = self.native_on(horizon_touch_native::MOD_ID);
        let actions = self.touch.set_mod_enabled(enabled);
        self.apply_touch_actions(actions);
    }

    #[cfg(all(feature = "native-d3d11", feature = "native-touch-mod"))]
    pub(super) fn release_failed_touch_module(&mut self) {
        let actions = self.touch.take_failure_actions();
        self.apply_touch_actions(actions);
    }

    fn apply_touch_actions(&mut self, actions: Vec<Action>) {
        for action in actions {
            let result = match action {
                Action::Jump(down) => self.mapped_key(
                    winit::keyboard::KeyCode::Space,
                    down,
                    false,
                    winit::keyboard::KeyCode::Space,
                ),
                Action::Attack(down) => self.play_mouse(winit::event::MouseButton::Left, down),
                Action::Use(down) => self.play_mouse(winit::event::MouseButton::Right, down),
                Action::Inventory => self.key_pressed(
                    winit::keyboard::KeyCode::KeyE,
                    winit::keyboard::KeyCode::KeyE,
                ),
                Action::Pause => self.pad_back(),
                Action::Sprint(down) => self.mapped_key(
                    winit::keyboard::KeyCode::ControlLeft,
                    down,
                    false,
                    winit::keyboard::KeyCode::ControlLeft,
                ),
                Action::Sneak(down) => self.mapped_key(
                    winit::keyboard::KeyCode::ShiftLeft,
                    down,
                    false,
                    winit::keyboard::KeyCode::ShiftLeft,
                ),
                Action::Hotbar(next) => {
                    self.selected = horizon_client::gamepad::hotbar_step(self.selected, next);
                    Ok(())
                }
                Action::Look(x, y) => {
                    self.turn_player(f64::from(x), f64::from(y));
                    Ok(())
                }
                Action::Back => self.pad_back(),
            };
            if let Err(e) = result {
                log::warn!("Horizon: touch action failed: {e}");
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn touch_mouse_compatibility_does_not_hide_touch_controls() {
        use winit::event::{DeviceId, ElementState, MouseButton};
        let mut controls = TouchControls::default();
        controls.hide_for_other_input();
        assert!(!controls.visible);
        controls.note_touch(7, TouchPhase::Started);
        assert!(controls.visible);
        let motion = WindowEvent::CursorMoved {
            device_id: DeviceId::dummy(),
            position: (20.0, 30.0).into(),
        };
        let press = WindowEvent::MouseInput {
            device_id: DeviceId::dummy(),
            state: ElementState::Pressed,
            button: MouseButton::Left,
        };
        assert!(controls.touch_compatible_mouse(&motion));
        assert!(controls.touch_compatible_mouse(&press));
        assert!(!controls.touch_compatible_mouse(&motion));
        controls.hide_for_other_input();
        assert!(!controls.visible);
        controls.note_touch(7, TouchPhase::Ended);
        controls.note_touch(8, TouchPhase::Started);
        assert!(controls.visible);
    }

    #[cfg(feature = "native-touch-mod")]
    #[test]
    fn disabling_touch_mod_releases_held_controls_and_reenabling_resets_state() {
        let mut controls = TouchControls::default();
        let size = [1000., 600.];
        controls.update(1, TouchPhase::Started, [108., 492.], size, true);
        controls.update(1, TouchPhase::Moved, [150., 440.], size, true);
        assert!(controls.movement.is_some());
        let releases = controls.set_mod_enabled(false);
        assert_eq!(controls.movement, None);
        assert!(releases.contains(&Action::Attack(false)));
        assert!(releases.contains(&Action::Jump(false)));
        assert!(
            controls
                .update(2, TouchPhase::Started, [934., 516.], size, true)
                .is_empty()
        );
        assert!(controls.set_mod_enabled(true).is_empty());
        assert_eq!(
            controls.update(2, TouchPhase::Started, [934., 516.], size, true),
            vec![Action::Jump(true)]
        );
    }

    #[test]
    fn movement_and_look_are_independent_and_cancellation_releases() {
        let mut c = TouchControls::default();
        let size = [1000., 600.];
        let (_, _, stick) = layout(size);
        c.update(1, TouchPhase::Started, stick, size, true);
        c.update(
            1,
            TouchPhase::Moved,
            [stick[0], stick[1] - 100.],
            size,
            true,
        );
        assert!(c.movement.unwrap()[1] > 0.9);
        c.update(2, TouchPhase::Started, [600., 300.], size, true);
        let actions = c.update(2, TouchPhase::Moved, [630., 312.], size, true);
        let mut look = (0., 0.);
        for action in actions {
            let Action::Look(x, y) = action else {
                panic!("unexpected touch action");
            };
            look.0 += x;
            look.1 += y;
        }
        assert_eq!(look, (60., 24.));
        c.update(1, TouchPhase::Cancelled, stick, size, true);
        assert_eq!(c.movement, None);
        assert!(c.contacts.contains_key(&2));
    }
    #[test]
    fn primary_game_touch_suppresses_mouse_but_menu_touch_does_not() {
        let mut c = TouchControls::default();
        let size = [1000., 600.];
        c.update(1, TouchPhase::Started, [600., 300.], size, true);
        assert_eq!(c.suppressed, 2);
        c.update(1, TouchPhase::Ended, [600., 300.], size, true);
        let mut menu = TouchControls::default();
        menu.update(3, TouchPhase::Started, [600., 300.], size, false);
        assert_eq!(menu.suppressed, 0);
    }
    #[test]
    fn held_actions_release_when_cancelled() {
        let mut c = TouchControls::default();
        let size = [1000., 600.];
        let (_, buttons, _) = layout(size);
        let jump = buttons[0].1;
        assert_eq!(
            c.update(9, TouchPhase::Started, jump, size, true),
            vec![Action::Jump(true)]
        );
        assert_eq!(
            c.update(9, TouchPhase::Cancelled, jump, size, true),
            vec![Action::Jump(false)]
        );
    }
    #[test]
    fn sprint_sneak_and_hotbar_controls_work() {
        let mut c = TouchControls::default();
        let size = [1000., 600.];
        let (_, buttons, _) = layout(size);
        for (i, down, up) in [
            (5, Action::Sprint(true), Action::Sprint(false)),
            (6, Action::Sneak(true), Action::Sneak(false)),
        ] {
            assert_eq!(
                c.update(i as u64, TouchPhase::Started, buttons[i].1, size, true),
                vec![down]
            );
            assert_eq!(
                c.update(i as u64, TouchPhase::Ended, buttons[i].1, size, true),
                vec![up]
            );
        }
        assert_eq!(
            c.update(7, TouchPhase::Started, buttons[7].1, size, true),
            vec![Action::Hotbar(false)]
        );
        assert_eq!(
            c.update(8, TouchPhase::Started, buttons[8].1, size, true),
            vec![Action::Hotbar(true)]
        );
    }
    #[test]
    fn movement_x_is_positive_left_and_menu_transition_releases_jump() {
        let mut c = TouchControls::default();
        let size = [1000., 600.];
        let (_, buttons, stick) = layout(size);
        c.update(1, TouchPhase::Started, stick, size, true);
        c.update(1, TouchPhase::Moved, [stick[0] + 70., stick[1]], size, true);
        assert!(c.movement.unwrap()[0] < 0.);
        let jump = buttons[0].1;
        assert_eq!(
            c.update(2, TouchPhase::Started, jump, size, true),
            vec![Action::Jump(true)]
        );
        assert_eq!(
            c.update(2, TouchPhase::Ended, jump, size, false),
            vec![Action::Jump(false)]
        );
    }
}
