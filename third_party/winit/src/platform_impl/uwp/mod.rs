//! UWP CoreWindow support. The host owns the window and pumps its dispatcher.
mod event_loop;
mod window;

pub(crate) use self::event_loop::{ActiveEventLoop, EventLoop, EventLoopProxy, OwnedDisplayHandle};
pub(crate) use self::window::Window;
pub(crate) use crate::cursor::{NoCustomCursor as PlatformCustomCursor, NoCustomCursor as PlatformCustomCursorSource};
pub(crate) use crate::icon::NoIcon as PlatformIcon;

use crate::dpi::{PhysicalPosition, PhysicalSize};
use crate::keyboard::Key;
use smol_str::SmolStr;

#[derive(Debug, Default, Clone, Copy, PartialEq, Eq, Hash)]
pub(crate) struct PlatformSpecificEventLoopAttributes;
#[derive(Debug, Default, Clone, Copy, PartialEq, Eq)]
pub(crate) struct PlatformSpecificWindowAttributes;
pub type OsError = std::io::Error;

#[derive(Clone, Copy, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct WindowId(u64);
impl WindowId { pub const fn dummy() -> Self { Self(0) } }
impl From<WindowId> for u64 { fn from(value: WindowId) -> u64 { value.0 } }
impl From<u64> for WindowId { fn from(value: u64) -> Self { Self(value) } }

#[derive(Clone, Copy, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct DeviceId;
impl DeviceId { pub const fn dummy() -> Self { Self } }

#[derive(Debug, Clone, Eq, PartialEq, Hash)]
pub struct KeyEventExtra {
    pub key_without_modifiers: Key,
    pub text_with_all_modifiers: Option<SmolStr>,
}

#[derive(Clone, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
pub struct MonitorHandle;
impl MonitorHandle {
    pub fn name(&self) -> Option<String> { Some("UWP display".to_owned()) }
    pub fn size(&self) -> PhysicalSize<u32> { let (w,h,_) = event_loop::metrics(); PhysicalSize::new(w,h) }
    pub fn position(&self) -> PhysicalPosition<i32> { (0,0).into() }
    pub fn scale_factor(&self) -> f64 { event_loop::metrics().2 }
    pub fn refresh_rate_millihertz(&self) -> Option<u32> { None }
    pub fn video_modes(&self) -> impl Iterator<Item=VideoModeHandle> { std::iter::empty() }
}

#[derive(Clone, Debug, Eq, Hash, PartialEq)]
pub struct VideoModeHandle;
impl VideoModeHandle {
    pub fn size(&self) -> PhysicalSize<u32> { MonitorHandle.size() }
    pub fn bit_depth(&self) -> u16 { 32 }
    pub fn refresh_rate_millihertz(&self) -> u32 { 60000 }
    pub fn monitor(&self) -> MonitorHandle { MonitorHandle }
}
