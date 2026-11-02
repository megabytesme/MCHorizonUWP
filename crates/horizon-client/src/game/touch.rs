//! Multitouch controls for the UWP CoreWindow backend.
use horizon_client::hud::{HudAtlas, HudBatch, rgba};
#[cfg(all(
    target_vendor = "uwp",
    feature = "native-d3d11",
    feature = "native-touch-mod"
))]
use horizon_touch_native as touch_module;
use std::collections::HashMap;
use winit::event::{Touch, TouchPhase, WindowEvent};

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
    #[cfg(all(
        target_vendor = "uwp",
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    module: Option<touch_module::TouchModule>,
    #[cfg(all(
        target_vendor = "uwp",
        feature = "native-d3d11",
        feature = "native-touch-mod"
    ))]
    release_after_failure: bool,
    #[cfg(all(target_vendor = "uwp", feature = "native-d3d11"))]
    pub(super) inventory_visible: bool,
}

impl Default for TouchControls {
    fn default() -> Self {
        #[cfg(all(
            target_vendor = "uwp",
            feature = "native-d3d11",
            feature = "native-touch-mod"
        ))]
        let module = Some(touch_module::TouchModule::new());
        Self {
            contacts: HashMap::new(),
            primary: None,
            primary_claimed: false,
            suppressed: 0,
            movement: None,
            used: false,
            #[cfg(all(
                target_vendor = "uwp",
                feature = "native-d3d11",
                feature = "native-touch-mod"
            ))]
            module,
            #[cfg(all(
                target_vendor = "uwp",
                feature = "native-d3d11",
                feature = "native-touch-mod"
            ))]
            release_after_failure: false,
            #[cfg(all(target_vendor = "uwp", feature = "native-d3d11"))]
            inventory_visible: false,
        }
    }
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
    fn update(
        &mut self,
        id: u64,
        phase: TouchPhase,
        p: [f32; 2],
        size: [f32; 2],
        playing: bool,
    ) -> Vec<Action> {
        #[cfg(all(
            target_vendor = "uwp",
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
            target_vendor = "uwp",
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
        target_vendor = "uwp",
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
        target_vendor = "uwp",
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
        if !self.used && !cfg!(target_arch = "arm") {
            return;
        }
        let size = [hud.width, hud.height];
        #[cfg(all(
            target_vendor = "uwp",
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
        #[cfg(all(target_vendor = "uwp", feature = "native-d3d11"))]
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
    pub(super) fn touch_event(&mut self, touch: Touch) {
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
    pub(super) fn release_failed_touch_module(&mut self) {
        let actions = self.touch.take_failure_actions();
        self.apply_touch_actions(actions);
    }

    fn apply_touch_actions(&mut self, actions: Vec<Action>) {
        for action in actions {
            let result = match action {
                Action::Jump(down) => self.mapped_key(winit::keyboard::KeyCode::Space, down, false),
                Action::Attack(down) => self.play_mouse(winit::event::MouseButton::Left, down),
                Action::Use(down) => self.play_mouse(winit::event::MouseButton::Right, down),
                Action::Inventory => self.key_pressed(winit::keyboard::KeyCode::KeyE),
                Action::Pause => self.pad_back(),
                Action::Sprint(down) => {
                    self.mapped_key(winit::keyboard::KeyCode::ControlLeft, down, false)
                }
                Action::Sneak(down) => {
                    self.mapped_key(winit::keyboard::KeyCode::ShiftLeft, down, false)
                }
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
        assert_eq!(
            c.update(2, TouchPhase::Moved, [630., 312.], size, true),
            vec![Action::Look(60., 24.)]
        );
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
