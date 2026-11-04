use std::cell::Cell;
use std::collections::{HashMap, VecDeque};
use std::marker::PhantomData;
use std::sync::{mpsc, Arc};
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;
use crate::event::{self, Event, WindowEvent};
use crate::event_loop::{self, ControlFlow, DeviceEvents};
use crate::keyboard::{Key, KeyCode, KeyLocation, NamedKey, NativeKeyCode, PhysicalKey};
use crate::window::{CustomCursor, CustomCursorSource, Theme};
use super::{DeviceId, KeyEventExtra, MonitorHandle, PlatformSpecificEventLoopAttributes, WindowId};

#[repr(C)]
#[derive(Clone, Copy, Default)]
struct NativeEvent {
    kind: u32, flags: u32, value: u32, scan_code: u32, pointer_id: u32,
    pointer_type: u32, buttons: u32, button_change: u32, wheel_delta: i32,
    x: f32, y: f32, width: f32, height: f32, scale_factor: f32,
}
unsafe extern "C" {
    pub(super) fn horizon_uwp_window_handle() -> *mut std::ffi::c_void;
    fn horizon_uwp_pump_events();
    fn horizon_uwp_pop_event(event: *mut NativeEvent) -> i32;
    fn horizon_uwp_get_metrics(width: *mut u32, height: *mut u32, scale: *mut f64);
}
pub(super) fn metrics() -> (u32,u32,f64) {
    let (mut w,mut h,mut s) = (0,0,1.0);
    unsafe { horizon_uwp_get_metrics(&mut w, &mut h, &mut s) };
    (w,h,s)
}

pub(super) struct Shared { pub created: AtomicBool, pub redraw: AtomicBool }
pub struct ActiveEventLoop { pub(super) shared: Arc<Shared>, flow: Cell<ControlFlow>, exiting: Cell<bool> }
pub struct EventLoop<T> { target: event_loop::ActiveEventLoop, sender: mpsc::Sender<T>, receiver: mpsc::Receiver<T> }
pub struct EventLoopProxy<T> { sender: mpsc::Sender<T> }
#[derive(Clone)] pub(crate) struct OwnedDisplayHandle;

// Unlike the desktop backends, this loop only borrows the host's existing
// dispatcher. Once it ends the launcher may start a fresh game in-process.
impl<T> Drop for EventLoop<T> {
    fn drop(&mut self) {
        event_loop::EventLoopBuilder::<()>::release_uwp_event_loop();
    }
}

impl<T: 'static> EventLoop<T> {
    pub(crate) fn new(_: &PlatformSpecificEventLoopAttributes) -> Result<Self, crate::error::EventLoopError> {
        if unsafe { horizon_uwp_window_handle() }.is_null() {
            return Err(crate::error::EventLoopError::Os(os_error!(std::io::Error::other("UWP host did not supply a CoreWindow"))));
        }
        let (sender, receiver) = mpsc::channel();
        Ok(Self { target: event_loop::ActiveEventLoop { p: ActiveEventLoop {
            shared: Arc::new(Shared { created: AtomicBool::new(false), redraw: AtomicBool::new(false) }),
            flow: Cell::new(ControlFlow::Poll), exiting: Cell::new(false),
        }, _marker: PhantomData }, sender, receiver })
    }
    pub fn run<F>(self, mut handler: F) -> Result<(), crate::error::EventLoopError>
    where F: FnMut(Event<T>, &event_loop::ActiveEventLoop) {
        let target = &self.target;
        handler(Event::NewEvents(event::StartCause::Init), target);
        handler(Event::Resumed, target);
        let id = crate::window::WindowId(WindowId(1));
        let device = crate::event::DeviceId(DeviceId);
        let mut pending_surrogate: Option<u16> = None;
        let mut contacts: HashMap<u32, (f64, f64)> = HashMap::new();
        let mut mouse_touch: Option<u32> = None;
        while !target.p.exiting() {
            unsafe { horizon_uwp_pump_events() };
            handler(Event::NewEvents(event::StartCause::Poll), target);
            let mut native = NativeEvent::default();
            while unsafe { horizon_uwp_pop_event(&mut native) } != 0 {
                if native.kind == 2 && native.value == 0 {
                    for (contact, (x, y)) in contacts.drain() {
                        handler(Event::WindowEvent { window_id: id, event: WindowEvent::Touch(event::Touch {
                            device_id: device, phase: event::TouchPhase::Cancelled,
                            location: (x, y).into(), force: None, id: contact as u64,
                        }) }, target);
                    }
                    if mouse_touch.take().is_some() {
                        handler(Event::WindowEvent { window_id: id, event: WindowEvent::MouseInput {
                            device_id: device, state: event::ElementState::Released, button: event::MouseButton::Left,
                        } }, target);
                    }
                }
                let touch = native.pointer_type == 0 && matches!(native.kind, 8 | 9 | 10 | 13);
                if touch {
                    let pos = (native.x as f64, native.y as f64);
                    let phase = match native.kind {
                        9 => {
                            if contacts.is_empty() { mouse_touch = Some(native.pointer_id); }
                            contacts.insert(native.pointer_id, pos);
                            event::TouchPhase::Started
                        },
                        10 => { contacts.remove(&native.pointer_id); event::TouchPhase::Ended },
                        13 => { contacts.remove(&native.pointer_id); event::TouchPhase::Cancelled },
                        _ => { if let Some(old) = contacts.get_mut(&native.pointer_id) { *old = pos; } event::TouchPhase::Moved },
                    };
                    handler(Event::WindowEvent { window_id: id, event: WindowEvent::Touch(event::Touch {
                        device_id: device, phase, location: pos.into(), force: None, id: native.pointer_id as u64,
                    }) }, target);
                }
                let mouse_compatible = !touch || mouse_touch == Some(native.pointer_id);
                if mouse_compatible && matches!(native.kind, 9 | 10 | 13) {
                    handler(Event::WindowEvent { window_id: id, event: WindowEvent::CursorMoved {
                        device_id: device, position: (native.x as f64, native.y as f64).into()
                    } }, target);
                }
                let window_event = if !mouse_compatible { None } else { match native.kind {
                    1 => Some(WindowEvent::Resized((native.width.max(0.0) as u32, native.height.max(0.0) as u32).into())),
                    2 => Some(WindowEvent::Focused(native.value != 0)),
                    3 => Some(WindowEvent::Occluded(native.value == 0)),
                    4 => Some(WindowEvent::CloseRequested),
                    5 | 6 => Some(WindowEvent::KeyboardInput { device_id: device, is_synthetic: false, event: key_event(native) }),
                    7 => {
                        let unit = native.value as u16;
                        if (0xd800..=0xdbff).contains(&unit) {
                            pending_surrogate = Some(unit);
                            None
                        } else {
                            let c = if let Some(high) = pending_surrogate.take() {
                                std::char::decode_utf16([high, unit]).next().and_then(Result::ok)
                            } else { char::from_u32(unit as u32) };
                            c.map(|c| WindowEvent::Ime(event::Ime::Commit(c.to_string())))
                        }
                    },
                    8 => {
                        let now = (native.x as f64, native.y as f64);
                        Some(WindowEvent::CursorMoved { device_id: device, position: now.into() })
                    },
                    9 | 10 => pointer_button(native.button_change).or_else(|| (native.pointer_type == 0).then_some(event::MouseButton::Left)).map(|button| WindowEvent::MouseInput { device_id: device, state: if native.kind == 9 { event::ElementState::Pressed } else { event::ElementState::Released }, button }),
                    11 => Some(WindowEvent::MouseWheel { device_id: device, delta: event::MouseScrollDelta::LineDelta(if native.flags & 4 != 0 { native.wheel_delta as f32 / 120.0 } else { 0.0 }, if native.flags & 4 == 0 { native.wheel_delta as f32 / 120.0 } else { 0.0 }), phase: event::TouchPhase::Moved }),
                    12 => { handler(Event::DeviceEvent { device_id: device, event: event::DeviceEvent::MouseMotion { delta: (native.x as f64, native.y as f64) } }, target); None },
                    13 if touch => Some(WindowEvent::MouseInput { device_id: device, state: event::ElementState::Released, button: event::MouseButton::Left }),
                    _ => None,
                }};
                if let Some(event) = window_event { handler(Event::WindowEvent { window_id: id, event }, target); }
                if touch && matches!(native.kind, 10 | 13) && mouse_touch == Some(native.pointer_id) { mouse_touch = None; }
            }
            for event in self.receiver.try_iter() { handler(Event::UserEvent(event), target); }
            if target.p.shared.created.load(Ordering::Acquire) && target.p.shared.redraw.swap(false, Ordering::AcqRel) {
                handler(Event::WindowEvent { window_id: id, event: WindowEvent::RedrawRequested }, target);
            }
            handler(Event::AboutToWait, target);
            // CoreDispatcher is polled on its owner thread; avoid spinning when idle.
            std::thread::sleep(Duration::from_millis(1));
        }
        handler(Event::LoopExiting, target);
        Ok(())
    }
    pub fn window_target(&self) -> &event_loop::ActiveEventLoop { &self.target }
    pub fn create_proxy(&self) -> EventLoopProxy<T> { EventLoopProxy { sender: self.sender.clone() } }
}
impl<T> EventLoopProxy<T> {
    pub fn send_event(&self, event: T) -> Result<(), event_loop::EventLoopClosed<T>> { self.sender.send(event).map_err(|e| event_loop::EventLoopClosed(e.0)) }
}
impl<T> Clone for EventLoopProxy<T> { fn clone(&self) -> Self { Self { sender: self.sender.clone() } } }
impl<T> Unpin for EventLoopProxy<T> {}
impl ActiveEventLoop {
    pub fn create_custom_cursor(&self, _: CustomCursorSource) -> CustomCursor { CustomCursor { inner: super::PlatformCustomCursor } }
    pub fn primary_monitor(&self) -> Option<MonitorHandle> { Some(MonitorHandle) }
    pub fn available_monitors(&self) -> VecDeque<MonitorHandle> { [MonitorHandle].into_iter().collect() }
    pub fn listen_device_events(&self, _: DeviceEvents) {}
    pub fn system_theme(&self) -> Option<Theme> { None }
    #[cfg(feature = "rwh_06")]
    pub fn raw_display_handle_rwh_06(&self) -> Result<rwh_06::RawDisplayHandle,rwh_06::HandleError> { Ok(rwh_06::WindowsDisplayHandle::new().into()) }
    pub fn set_control_flow(&self, flow: ControlFlow) { self.flow.set(flow); }
    pub fn control_flow(&self) -> ControlFlow { self.flow.get() }
    pub(crate) fn exit(&self) { self.exiting.set(true); }
    pub(crate) fn exiting(&self) -> bool { self.exiting.get() }
    pub(crate) fn owned_display_handle(&self) -> OwnedDisplayHandle { OwnedDisplayHandle }
}
impl OwnedDisplayHandle {
    #[cfg(feature = "rwh_06")]
    pub fn raw_display_handle_rwh_06(&self) -> Result<rwh_06::RawDisplayHandle,rwh_06::HandleError> { Ok(rwh_06::WindowsDisplayHandle::new().into()) }
}

fn pointer_button(value: u32) -> Option<event::MouseButton> {
    match value { 1|2 => Some(event::MouseButton::Left), 3|4 => Some(event::MouseButton::Right), 5|6 => Some(event::MouseButton::Middle), 7|8 => Some(event::MouseButton::Back), 9|10 => Some(event::MouseButton::Forward), _ => None }
}
fn key_event(e: NativeEvent) -> event::KeyEvent {
    let keycode = match e.value {
        0x41 => Some(KeyCode::KeyA), 0x42 => Some(KeyCode::KeyB), 0x43 => Some(KeyCode::KeyC), 0x44 => Some(KeyCode::KeyD), 0x45 => Some(KeyCode::KeyE), 0x46 => Some(KeyCode::KeyF), 0x47 => Some(KeyCode::KeyG), 0x48 => Some(KeyCode::KeyH), 0x49 => Some(KeyCode::KeyI), 0x4a => Some(KeyCode::KeyJ), 0x4b => Some(KeyCode::KeyK), 0x4c => Some(KeyCode::KeyL), 0x4d => Some(KeyCode::KeyM), 0x4e => Some(KeyCode::KeyN), 0x4f => Some(KeyCode::KeyO), 0x50 => Some(KeyCode::KeyP), 0x51 => Some(KeyCode::KeyQ), 0x52 => Some(KeyCode::KeyR), 0x53 => Some(KeyCode::KeyS), 0x54 => Some(KeyCode::KeyT), 0x55 => Some(KeyCode::KeyU), 0x56 => Some(KeyCode::KeyV), 0x57 => Some(KeyCode::KeyW), 0x58 => Some(KeyCode::KeyX), 0x59 => Some(KeyCode::KeyY), 0x5a => Some(KeyCode::KeyZ),
        0x30 => Some(KeyCode::Digit0), 0x31 => Some(KeyCode::Digit1), 0x32 => Some(KeyCode::Digit2), 0x33 => Some(KeyCode::Digit3), 0x34 => Some(KeyCode::Digit4), 0x35 => Some(KeyCode::Digit5), 0x36 => Some(KeyCode::Digit6), 0x37 => Some(KeyCode::Digit7), 0x38 => Some(KeyCode::Digit8), 0x39 => Some(KeyCode::Digit9),
        0x70 => Some(KeyCode::F1), 0x71 => Some(KeyCode::F2), 0x72 => Some(KeyCode::F3), 0x73 => Some(KeyCode::F4), 0x74 => Some(KeyCode::F5), 0x75 => Some(KeyCode::F6), 0x76 => Some(KeyCode::F7), 0x77 => Some(KeyCode::F8), 0x78 => Some(KeyCode::F9), 0x79 => Some(KeyCode::F10), 0x7a => Some(KeyCode::F11), 0x7b => Some(KeyCode::F12),
        0x20 => Some(KeyCode::Space), 0x1b => Some(KeyCode::Escape), 0x0d => Some(KeyCode::Enter), 0x08 => Some(KeyCode::Backspace), 0x09 => Some(KeyCode::Tab), 0x10 if e.scan_code == 0x36 => Some(KeyCode::ShiftRight), 0x10 => Some(KeyCode::ShiftLeft), 0x11 if e.flags & 1 != 0 => Some(KeyCode::ControlRight), 0x11 => Some(KeyCode::ControlLeft), 0x12 if e.flags & 1 != 0 => Some(KeyCode::AltRight), 0x12 => Some(KeyCode::AltLeft), 0x25 => Some(KeyCode::ArrowLeft), 0x26 => Some(KeyCode::ArrowUp), 0x27 => Some(KeyCode::ArrowRight), 0x28 => Some(KeyCode::ArrowDown), _ => None,
    };
    let logical = match e.value {
        0x20 => Key::Named(NamedKey::Space), 0x1b => Key::Named(NamedKey::Escape), 0x0d => Key::Named(NamedKey::Enter), 0x08 => Key::Named(NamedKey::Backspace), 0x09 => Key::Named(NamedKey::Tab), 0x10 => Key::Named(NamedKey::Shift), 0x11 => Key::Named(NamedKey::Control), 0x12 => Key::Named(NamedKey::Alt), 0x25 => Key::Named(NamedKey::ArrowLeft), 0x26 => Key::Named(NamedKey::ArrowUp), 0x27 => Key::Named(NamedKey::ArrowRight), 0x28 => Key::Named(NamedKey::ArrowDown),
        0x41..=0x5a => Key::Character(char::from_u32(e.value + 32).unwrap().to_string().into()),
        0x30..=0x39 => Key::Character(char::from_u32(e.value).unwrap().to_string().into()),
        _ => Key::Unidentified(crate::keyboard::NativeKey::Unidentified),
    };
    event::KeyEvent {
        physical_key: keycode.map(PhysicalKey::Code).unwrap_or(PhysicalKey::Unidentified(NativeKeyCode::Windows(e.scan_code as u16))),
        logical_key: logical.clone(), text: None, location: KeyLocation::Standard,
        state: if e.kind == 5 { event::ElementState::Pressed } else { event::ElementState::Released },
        repeat: e.flags & 2 != 0,
        platform_specific: KeyEventExtra { key_without_modifiers: logical, text_with_all_modifiers: None },
    }
}
