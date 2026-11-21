use mod_api::{
    self, ClientHost, ClientStoppingEvent, GuiRenderPostEvent, Hooks, Registrar,
    RegistrationContext, RenderFramePreEvent, SettingSpec,
};
use std::sync::Mutex;
use std::time::{Duration, Instant};

const ID: &str = "horizon_gyro_aim";
const MAX_SAMPLE_GAP: Duration = Duration::from_millis(240);
const CALIBRATION_TIME: f32 = 0.65;
const CALIBRATION_MAX_SPEED: f32 = 6.0;

#[derive(Default)]
struct GyroAim(Mutex<State>);

#[derive(Default)]
struct State {
    active: bool,
    next_probe: Option<Instant>,
    previous_sample: Option<Instant>,
    last_orientation: Option<i32>,
    unavailable_since: Option<Instant>,
    motion: Motion,
}

impl State {
    fn stop(&mut self) {
        if self.active || self.next_probe.is_some() {
            sensor::enable(false);
        }
        self.active = false;
        self.next_probe = None;
        self.unavailable_since = None;
        self.previous_sample = None;
        self.motion.reset();
    }

    fn start(&mut self, now: Instant) {
        if self.next_probe.is_some_and(|probe| now < probe) {
            return;
        }
        self.active = sensor::enable(true);
        self.next_probe = Some(now + Duration::from_secs(if self.active { 1 } else { 3 }));
        if self.active {
            self.unavailable_since = None;
        } else {
            self.unavailable_since.get_or_insert(now);
        }
    }
}

#[derive(Clone, Copy)]
struct Settings {
    sensitivity: [f32; 2],
    invert: [bool; 2],
    smoothing_ms: f32,
    deadzone: f32,
}

impl Settings {
    fn read(host: &dyn ClientHost) -> Self {
        Self {
            sensitivity: [
                host.setting_f64("horizontal").unwrap_or(1.0) as f32,
                host.setting_f64("vertical").unwrap_or(1.0) as f32,
            ],
            invert: [
                host.setting_bool("invert_x").unwrap_or(false),
                host.setting_bool("invert_y").unwrap_or(false),
            ],
            smoothing_ms: host.setting_f64("smoothing").unwrap_or(30.0) as f32,
            deadzone: host.setting_f64("deadzone").unwrap_or(1.0) as f32,
        }
    }
}

#[derive(Default)]
struct Motion {
    bias: [f32; 2],
    smoothed: [f32; 2],
    calibration: Option<Calibration>,
}

#[derive(Default)]
struct Calibration {
    total_delta: [f32; 2],
    seconds: f32,
}

impl Motion {
    fn reset(&mut self) {
        self.smoothed = [0.0; 2];
        if let Some(calibration) = &mut self.calibration {
            *calibration = Calibration::default();
        }
    }

    fn turn(&mut self, delta: [f32; 2], dt: f32, settings: Settings) -> [f32; 2] {
        let rate = delta.map(|degrees| degrees / dt);
        if let Some(calibration) = &mut self.calibration {
            if rate
                .into_iter()
                .any(|speed| speed.abs() > CALIBRATION_MAX_SPEED)
            {
                *calibration = Calibration::default();
                return [0.0; 2];
            }
            calibration.seconds += dt;
            for (total, degrees) in calibration.total_delta.iter_mut().zip(delta) {
                *total += degrees;
            }
            if calibration.seconds >= CALIBRATION_TIME {
                self.bias = calibration
                    .total_delta
                    .map(|total| total / calibration.seconds);
                self.calibration = None;
            }
            return [0.0; 2];
        }

        let blend = if settings.smoothing_ms == 0.0 {
            1.0
        } else {
            1.0 - (-1000.0 * dt / settings.smoothing_ms).exp()
        };
        std::array::from_fn(|axis| {
            let corrected = rate[axis] - self.bias[axis];
            if corrected.abs() <= settings.deadzone {
                self.smoothed[axis] = 0.0;
                return 0.0;
            }
            self.smoothed[axis] += blend * (corrected - self.smoothed[axis]);
            let sign = if settings.invert[axis] { -1.0 } else { 1.0 };
            (self.smoothed[axis] * dt * sign * settings.sensitivity[axis]).clamp(-30.0, 30.0)
        })
    }
}

fn screen_axes([x, y]: [f32; 2], rotation: i32) -> [f32; 2] {
    let (right, up) = match rotation.rem_euclid(4) {
        0 => (x, y),
        1 => (-y, x),
        2 => (-x, -y),
        _ => (y, -x),
    };
    [-up.to_degrees(), -right.to_degrees()]
}

#[mod_api::horizon_mod]
impl Hooks for GyroAim {
    fn register(&self, registrar: &mut dyn Registrar, _: &RegistrationContext) {
        for setting in [
            SettingSpec::toggle("enabled", "Gyroscope aiming", false),
            SettingSpec::range("horizontal", "Horizontal sensitivity", 0.25, 3.0, 0.05, 1.0),
            SettingSpec::range("vertical", "Vertical sensitivity", 0.25, 3.0, 0.05, 1.0),
            SettingSpec::toggle("invert_x", "Invert horizontal gyro", false),
            SettingSpec::toggle("invert_y", "Invert vertical gyro", false),
            SettingSpec::range("smoothing", "Gyro smoothing (ms)", 0.0, 120.0, 5.0, 30.0),
            SettingSpec::range("deadzone", "Gyro deadzone (degrees/s)", 0.0, 8.0, 0.25, 1.0),
            SettingSpec::toggle("using_item", "Gyro only while using an item", false),
            SettingSpec::toggle("calibrate", "Calibrate gyro when play resumes", false),
        ] {
            let _ = registrar.register_setting(setting);
        }
    }

    fn render_frame_pre(&self, host: &mut dyn ClientHost, _: &mut RenderFramePreEvent) {
        let mut state = self
            .0
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        if !host.on(ID)
            || !host.playing()
            || !host.window_focused()
            || host.is_paused()
            || host.observed_screen_id().is_some()
            || !host.setting_bool("enabled").unwrap_or(false)
        {
            state.stop();
            return;
        }
        let Some(player) = host.local_player().map(|player| player.entity()) else {
            state.stop();
            return;
        };
        if host.camera_entity() != Some(player) {
            state.stop();
            return;
        }

        let calibrate = host.setting_bool("calibrate").unwrap_or(false);
        if !calibrate
            && state.motion.calibration.is_none()
            && host.setting_bool("using_item").unwrap_or(false)
            && host.local_item_use().is_none()
        {
            state.stop();
            return;
        }

        let now = Instant::now();
        state.start(now);
        if !state.active {
            return;
        }
        if calibrate {
            host.set_setting(ID, "calibrate", "false");
            state.motion.reset();
            state.motion.calibration = Some(Calibration::default());
        }
        if state.previous_sample.is_some_and(|previous| {
            now.saturating_duration_since(previous) < Duration::from_millis(1)
        }) {
            return;
        }
        let Some((sample, rotation)) = sensor::take() else {
            if state
                .previous_sample
                .is_some_and(|previous| now.saturating_duration_since(previous) > MAX_SAMPLE_GAP)
            {
                state.previous_sample = None;
                state.motion.reset();
            }
            return;
        };
        let previous = state.previous_sample.replace(now).unwrap_or(now);
        let orientation = rotation >> 2;
        if state
            .last_orientation
            .replace(orientation)
            .is_some_and(|previous| previous != orientation)
        {
            state.motion.bias = [0.0; 2];
            state.motion.reset();
            return;
        }
        let dt = now.saturating_duration_since(previous);
        if dt.is_zero() || dt > MAX_SAMPLE_GAP {
            state.motion.reset();
            return;
        }
        let [yaw_delta, pitch_delta] = state.motion.turn(
            screen_axes(sample, rotation),
            dt.as_secs_f32(),
            Settings::read(host),
        );
        if (yaw_delta != 0.0 || pitch_delta != 0.0)
            && let Some([yaw, pitch]) = player.rotation(host)
        {
            let _ = host.set_local_rotation(yaw + yaw_delta, pitch + pitch_delta);
        }
    }

    fn gui_render_post(&self, host: &dyn ClientHost, event: &mut GuiRenderPostEvent<'_>) {
        if event.gui_hidden || !event.focused || !host.playing() {
            return;
        }
        let state = self
            .0
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let status = if state.active && state.motion.calibration.is_some() {
            Some((
                "mod.horizon_gyro_aim.calibrate",
                "Hold the device still to calibrate the gyroscope",
            ))
        } else if state
            .unavailable_since
            .is_some_and(|since| since.elapsed() < Duration::from_secs(6))
        {
            Some((
                "mod.horizon_gyro_aim.unavailable",
                "Gyroscope unavailable on this device",
            ))
        } else {
            None
        };
        if let Some((key, fallback)) = status {
            let translated = host.translate(key);
            event.batch.text(
                translated.as_deref().unwrap_or(fallback),
                [8.0, 8.0],
                0xffff_ffff,
                true,
            );
        }
    }

    fn client_stopping(&self, _: &mut dyn ClientHost, _: &mut ClientStoppingEvent) {
        self.0
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .stop();
    }
}

#[cfg(target_vendor = "uwp")]
mod sensor {
    unsafe extern "C" {
        fn horizon_uwp_gyro_enable(enabled: i32) -> i32;
        fn horizon_uwp_gyro_take(x: *mut f32, y: *mut f32, rotation: *mut i32) -> i32;
    }

    pub(super) fn enable(enabled: bool) -> bool {
        unsafe { horizon_uwp_gyro_enable(i32::from(enabled)) != 0 }
    }

    pub(super) fn take() -> Option<([f32; 2], i32)> {
        let (mut x, mut y, mut rotation) = (0.0, 0.0, 0);
        let sampled = unsafe { horizon_uwp_gyro_take(&mut x, &mut y, &mut rotation) };
        (sampled != 0 && x.is_finite() && y.is_finite()).then_some(([x, y], rotation))
    }
}

#[cfg(not(target_vendor = "uwp"))]
mod sensor {
    pub(super) fn enable(_: bool) -> bool {
        false
    }

    pub(super) fn take() -> Option<([f32; 2], i32)> {
        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn settings() -> Settings {
        Settings {
            sensitivity: [1.5, 0.5],
            invert: [false, true],
            smoothing_ms: 0.0,
            deadzone: 1.0,
        }
    }

    #[test]
    fn camera_axes_turn_with_the_display() {
        let angles = [90_f32.to_radians(), 45_f32.to_radians()];
        assert_eq!(screen_axes(angles, 1), [-90.0, 45.0]);
        assert_eq!(screen_axes(angles, 3), [90.0, -45.0]);
    }

    #[test]
    fn motion_applies_per_axis_settings_and_stops_without_a_smoothing_tail() {
        let mut motion = Motion::default();
        assert_eq!(motion.turn([2.0, -4.0], 0.1, settings()), [3.0, 2.0]);
        let smooth = Settings {
            smoothing_ms: 30.0,
            ..settings()
        };
        assert_eq!(motion.turn([0.0, 0.0], 0.1, smooth), [0.0; 2]);
        assert_eq!(motion.smoothed, [0.0; 2]);
        assert_eq!(motion.turn([50.0, 90.0], 0.1, settings()), [30.0, -30.0]);
    }

    #[test]
    fn calibration_rejects_motion_and_removes_stationary_bias() {
        let mut motion = Motion {
            calibration: Some(Calibration::default()),
            ..Motion::default()
        };
        let raw = Settings {
            deadzone: 0.1,
            ..settings()
        };
        motion.turn([1.0, 0.0], 0.1, raw);
        assert_eq!(motion.calibration.as_ref().unwrap().seconds, 0.0);
        for _ in 0..7 {
            assert_eq!(motion.turn([0.125, -0.0625], 0.1, raw), [0.0; 2]);
        }
        assert!(motion.calibration.is_none());
        assert_eq!(motion.turn([0.125, -0.0625], 0.1, raw), [0.0; 2]);
    }
}
