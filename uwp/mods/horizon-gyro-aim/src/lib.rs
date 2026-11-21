#[cfg(not(target_vendor = "uwp"))]
use mod_api::ConfigPath;
use mod_api::{
    self, ClientHost, ClientStoppingEvent, EntityRef, GuiRenderPostEvent, Hooks, Registrar,
    RegistrationContext, RenderFramePreEvent, SettingSpec,
};
use std::collections::VecDeque;
use std::fmt::Write;
use std::sync::Mutex;
use std::time::{Duration, Instant};

const ID: &str = "horizon_gyro_aim";
const MAX_SAMPLE_GAP: Duration = Duration::from_millis(240);
const CALIBRATION_TIME: f32 = 0.65;
const CALIBRATION_MAX_SPEED: f32 = 6.0;
const TRACE_ROWS: usize = 360;
const TRACE_PERIOD_MS: u128 = 200;

#[derive(Clone, Copy)]
struct MotionFrame {
    sample: [f32; 2],
    rotation: i32,
    interval: Duration,
    delta: [f32; 2],
    before: Option<[f32; 2]>,
    accepted: bool,
}

struct MotionTrace {
    started: Instant,
    next_row_ms: u128,
    next_save_ms: u128,
    rows: VecDeque<String>,
    #[cfg(target_vendor = "uwp")]
    reported_write_failure: bool,
}

impl MotionTrace {
    fn new(now: Instant) -> Self {
        Self {
            started: now,
            next_row_ms: 0,
            next_save_ms: 0,
            rows: VecDeque::new(),
            #[cfg(target_vendor = "uwp")]
            reported_write_failure: false,
        }
    }

    fn record(
        &mut self,
        now: Instant,
        host: &dyn ClientHost,
        player: EntityRef,
        sensor_active: bool,
        frame: Option<MotionFrame>,
    ) {
        let elapsed_ms = now.saturating_duration_since(self.started).as_millis();
        if elapsed_ms < self.next_row_ms {
            return;
        }
        self.next_row_ms = elapsed_ms + TRACE_PERIOD_MS;
        if self.rows.len() == TRACE_ROWS {
            self.rows.pop_front();
        }
        let after = player.rotation(host).unwrap_or([f32::NAN; 2]);
        let before = frame.and_then(|frame| frame.before).unwrap_or(after);
        let [sensor_x, sensor_y] = frame.map_or([f32::NAN; 2], |frame| frame.sample);
        let [yaw_delta, pitch_delta] = frame.map_or([0.0; 2], |frame| frame.delta);
        let orientation = frame.map_or(-1, |frame| frame.rotation >> 2);
        let interval_ms = frame.map_or(0, |frame| frame.interval.as_millis());
        let accepted = frame.is_some_and(|frame| frame.accepted);
        self.rows.push_back(format!(
            "{elapsed_ms},{},{},{},{},{sensor_active},{orientation},{interval_ms},{sensor_x:.6},{sensor_y:.6},{yaw_delta:.4},{pitch_delta:.4},{:.4},{:.4},{accepted},{:.4},{:.4}",
            host.setting_bool("enabled").unwrap_or(false),
            host.playing(),
            host.window_focused(),
            host.observed_screen_id().is_some(),
            before[0], before[1], after[0], after[1],
        ));
        if elapsed_ms >= self.next_save_ms {
            self.next_save_ms = elapsed_ms + 1000;
            self.save(host);
        }
    }

    fn save(&mut self, _host: &dyn ClientHost) {
        let mut csv = String::from("elapsed_ms,enabled,playing,focused,screen_open,sensor_active,orientation_flags,frame_interval_ms,sensor_x_rad,sensor_y_rad,requested_yaw_deg,requested_pitch_deg,before_yaw_deg,before_pitch_deg,accepted,after_yaw_deg,after_pitch_deg
");
        for row in &self.rows {
            let _ = writeln!(csv, "{row}");
        }
        #[cfg(target_vendor = "uwp")]
        match trace_file::write(csv.as_bytes()) {
            Ok(()) => self.reported_write_failure = false,
            Err(error) if !self.reported_write_failure => {
                self.reported_write_failure = true;
                eprintln!("Gyro diagnostics could not write motion.csv: {error}");
            }
            Err(_) => {}
        }
        #[cfg(not(target_vendor = "uwp"))]
        if let Ok(path) = ConfigPath::new("motion.csv") {
            let _ = _host.data_files().write(&path, csv.as_bytes());
        }
    }
}

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
    trace: Option<MotionTrace>,
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

impl State {
    fn aim(
        &mut self,
        host: &mut dyn ClientHost,
        player: Option<EntityRef>,
        now: Instant,
    ) -> Option<MotionFrame> {
        if !host.on(ID)
            || !host.playing()
            || !host.window_focused()
            || host.is_paused()
            || host.observed_screen_id().is_some()
            || !host.setting_bool("enabled").unwrap_or(false)
        {
            self.stop();
            return None;
        }
        let Some(player) = player else {
            self.stop();
            return None;
        };
        if host.camera_entity() != Some(player) {
            self.stop();
            return None;
        }

        let calibrate = host.setting_bool("calibrate").unwrap_or(false);
        if !calibrate
            && self.motion.calibration.is_none()
            && host.setting_bool("using_item").unwrap_or(false)
            && host.local_item_use().is_none()
        {
            self.stop();
            return None;
        }

        self.start(now);
        if !self.active {
            return None;
        }
        if calibrate {
            host.set_setting(ID, "calibrate", "false");
            self.motion.reset();
            self.motion.calibration = Some(Calibration::default());
        }
        if self.previous_sample.is_some_and(|previous| {
            now.saturating_duration_since(previous) < Duration::from_millis(1)
        }) {
            return None;
        }
        let Some((sample, rotation)) = sensor::take() else {
            if self
                .previous_sample
                .is_some_and(|previous| now.saturating_duration_since(previous) > MAX_SAMPLE_GAP)
            {
                self.previous_sample = None;
                self.motion.reset();
            }
            return None;
        };
        let previous = self.previous_sample.replace(now).unwrap_or(now);
        let orientation = rotation >> 2;
        if self
            .last_orientation
            .replace(orientation)
            .is_some_and(|previous| previous != orientation)
        {
            self.motion.bias = [0.0; 2];
            self.motion.reset();
            return None;
        }
        let dt = now.saturating_duration_since(previous);
        if dt.is_zero() || dt > MAX_SAMPLE_GAP {
            self.motion.reset();
            return None;
        }
        let delta = self.motion.turn(
            screen_axes(sample, rotation),
            dt.as_secs_f32(),
            Settings::read(host),
        );
        let before = if delta != [0.0; 2] {
            player.rotation(host)
        } else {
            None
        };
        let accepted = before.is_some_and(|[yaw, pitch]| {
            host.set_local_rotation(yaw + delta[0], pitch + delta[1])
                .is_ok()
        });
        Some(MotionFrame {
            sample,
            rotation,
            interval: dt,
            delta,
            before,
            accepted,
        })
    }
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
            SettingSpec::toggle("capture_diagnostics", "Capture gyro diagnostics", false),
        ] {
            let _ = registrar.register_setting(setting);
        }
    }

    fn render_frame_pre(&self, host: &mut dyn ClientHost, _: &mut RenderFramePreEvent) {
        let mut state = self
            .0
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let now = Instant::now();
        let player = host.local_player().map(|player| player.entity());
        let frame = state.aim(host, player, now);
        if let Some(player) = player {
            if host.setting_bool("capture_diagnostics").unwrap_or(false) {
                let active = state.active;
                state
                    .trace
                    .get_or_insert_with(|| MotionTrace::new(now))
                    .record(now, host, player, active, frame);
            } else if let Some(mut trace) = state.trace.take() {
                trace.save(host);
            }
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
            let label = translated.as_deref().unwrap_or(fallback);
            let [width, height] = event.batch.size();
            let x = ((width - event.batch.text_width(label)) * 0.5).max(0.0);
            event
                .batch
                .text(label, [x, (height - 56.0).max(4.0)], 0xffff_ffff, true);
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
mod trace_file {
    //! The trusted UWP gyro mod owns just this opt-in diagnostics file.

    use std::fs::{self, OpenOptions};
    use std::io::{self, Write};
    use std::os::windows::fs::MetadataExt;
    use std::path::{Path, PathBuf};
    use std::sync::atomic::{AtomicU32, Ordering};

    const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x0400;
    static NEXT_TEMP: AtomicU32 = AtomicU32::new(0);

    pub(super) fn write(csv: &[u8]) -> io::Result<()> {
        let root = std::env::var_os("HORIZON_UWP_GYRO_TRACE_ROOT")
            .map(PathBuf::from)
            .filter(|path| path.is_absolute())
            .ok_or_else(|| io::Error::other("the app-local gyro trace root is unavailable"))?;
        write_in_game(&root, csv)
    }

    fn write_in_game(root: &Path, csv: &[u8]) -> io::Result<()> {
        ordinary_directory(root)?;
        let mut directory = root.to_path_buf();
        for component in ["horizon-mod-data", super::ID] {
            directory.push(component);
            match fs::create_dir(&directory) {
                Ok(()) => {}
                Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {}
                Err(error) => return Err(error),
            }
            ordinary_directory(&directory)?;
        }

        let path = directory.join("motion.csv");
        let sequence = NEXT_TEMP.fetch_add(1, Ordering::Relaxed);
        let temporary = directory.join(format!(".motion-{}-{sequence}.tmp", std::process::id()));
        let mut file = OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&temporary)?;
        let written = file.write_all(csv);
        drop(file);
        let result = written.and_then(|()| fs::rename(&temporary, &path));
        if result.is_err() {
            let _ = fs::remove_file(temporary);
        }
        result
    }

    fn ordinary_directory(path: &Path) -> io::Result<()> {
        let metadata = fs::symlink_metadata(path)?;
        if metadata.is_dir() && metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT == 0 {
            Ok(())
        } else {
            Err(io::Error::other(
                "the gyro trace directory is not an ordinary directory",
            ))
        }
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
    fn display_aligned_samples_are_not_rotated_again_in_either_landscape() {
        let angles = [45_f32.to_radians(), -90_f32.to_radians()];
        for orientation in [1, 4] {
            assert_eq!(screen_axes(angles, orientation << 2), [90.0, -45.0]);
        }
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
