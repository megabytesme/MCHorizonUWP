//! Experimental separately distributed touch guest; host ABI v1, not Horizon's
//! stock mod interface. Must run single-threaded with bounded fuel and memory.
#[link(wasm_import_module = "horizon_touch_v1")]
extern "C" {
    fn action(code: i32, value: f32);
    fn control(id: i32, x: f32, y: f32, radius: f32);
}
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
const LOOK: i32 = 20;
const STICK: i32 = 21;
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
    x: 0.0,
    y: 0.0,
};
static mut FINGERS: [Finger; 16] = [EMPTY; 16];
fn emit(code: i32, value: f32) {
    unsafe { action(code, value) }
}
fn geometry(w: f32, h: f32) -> [(i32, f32, f32); 10] {
    let s = w.min(h);
    [
        (JUMP, w - s * 0.11, h - s * 0.14),
        (ATTACK, w - s * 0.27, h - s * 0.25),
        (USE, w - s * 0.11, h - s * 0.36),
        (INVENTORY, w - s * 0.08, s * 0.08),
        (PAUSE, s * 0.08, s * 0.08),
        (SPRINT, s * 0.09, h - s * 0.43),
        (SNEAK, s * 0.26, h - s * 0.43),
        (PREV, w * 0.5 - s * 0.20, h - s * 0.075),
        (NEXT, w * 0.5 + s * 0.20, h - s * 0.075),
        (STICK, s * 0.18, h - s * 0.18),
    ]
}
#[no_mangle]
pub extern "C" fn mod_api_version() -> i32 {
    1
}
/// Host calls only for gameplay. phase: 0=start, 1=move, 2=end, 3=cancel.
/// Returns 1 if claimed, 0 if invalid/capacity exceeded.
#[no_mangle]
pub extern "C" fn touch(id: i32, phase: i32, x: f32, y: f32, w: f32, h: f32) -> i32 {
    if id < 0 || ![x, y, w, h].iter().all(|v| v.is_finite()) || w <= 0.0 || h <= 0.0 {
        return 0;
    }
    let s = w.min(h);
    let radius = s * 0.075;
    let positions = geometry(w, h);
    unsafe {
        let existing = (0..16).find(|&i| FINGERS[i].id == id);
        match phase {
            0 => {
                if existing.is_some() {
                    return 0;
                }
                let Some(slot) = (0..16).find(|&i| FINGERS[i].id < 0) else {
                    return 0;
                };
                let mut role = LOOK;
                for &(button, bx, by) in &positions[..9] {
                    let dx = x - bx;
                    let dy = y - by;
                    if dx * dx + dy * dy <= (radius * 1.25).powi(2) {
                        role = button;
                        break;
                    }
                }
                let (_, sx, sy) = positions[9];
                let dx = x - sx;
                let dy = y - sy;
                if role == LOOK
                    && dx * dx + dy * dy <= (radius * 2.2).powi(2)
                    && !(0..16).any(|i| FINGERS[i].role == STICK)
                {
                    role = STICK;
                }
                let held = matches!(role, JUMP | ATTACK | USE | SPRINT | SNEAK)
                    && (0..16).any(|i| FINGERS[i].id >= 0 && FINGERS[i].role == role);
                FINGERS[slot] = Finger { id, role, x, y };
                if role <= NEXT && !held {
                    emit(role, 1.0);
                }
                1
            }
            1 => {
                let Some(slot) = existing else {
                    return 0;
                };
                let f = FINGERS[slot];
                if f.role == STICK {
                    let (_, sx, sy) = positions[9];
                    let mut vx = (x - sx) / (radius * 1.8);
                    let mut vy = (sy - y) / (radius * 1.8);
                    let length = (vx * vx + vy * vy).sqrt();
                    if length > 1.0 {
                        vx /= length;
                        vy /= length;
                    }
                    emit(MOVE_X, -vx);
                    emit(MOVE_Y, vy);
                } else if f.role == LOOK {
                    emit(LOOK_X, (x - f.x) * 1200.0 / s);
                    emit(LOOK_Y, (y - f.y) * 1200.0 / s);
                }
                FINGERS[slot].x = x;
                FINGERS[slot].y = y;
                1
            }
            2 | 3 => {
                let Some(slot) = existing else {
                    return 0;
                };
                let role = FINGERS[slot].role;
                FINGERS[slot] = EMPTY;
                if role == STICK {
                    emit(MOVE_X, 0.0);
                    emit(MOVE_Y, 0.0);
                } else if matches!(role, JUMP | ATTACK | USE | SPRINT | SNEAK)
                    && !(0..16).any(|i| FINGERS[i].id >= 0 && FINGERS[i].role == role)
                {
                    emit(role, 0.0);
                }
                1
            }
            _ => 0,
        }
    }
}
/// Host invokes while gameplay is visible; it owns actual glyphs/textures.
#[no_mangle]
pub extern "C" fn draw(w: f32, h: f32) {
    if !w.is_finite() || !h.is_finite() || w <= 0.0 || h <= 0.0 {
        return;
    }
    let radius = w.min(h) * 0.075;
    for (id, x, y) in geometry(w, h) {
        unsafe { control(id, x, y, radius) }
    }
}
#[no_mangle]
pub extern "C" fn reset() {
    unsafe {
        for i in 0..16 {
            let f = FINGERS[i];
            if f.id >= 0 {
                if f.role == STICK {
                    emit(MOVE_X, 0.0);
                    emit(MOVE_Y, 0.0);
                } else if matches!(f.role, JUMP | ATTACK | USE | SPRINT | SNEAK) {
                    emit(f.role, 0.0);
                }
                FINGERS[i] = EMPTY;
            }
        }
    }
}
