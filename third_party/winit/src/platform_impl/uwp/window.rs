use std::collections::VecDeque;
use std::sync::{Arc, Mutex};
use std::sync::atomic::Ordering;
use crate::cursor::Cursor;
use crate::dpi::{PhysicalPosition, PhysicalSize, Position, Size};
use crate::{error, window};
use super::{ActiveEventLoop, MonitorHandle, WindowId};
use super::event_loop::{metrics, Shared};
use crate::platform_impl::Fullscreen;
unsafe extern "C" {
    fn horizon_uwp_set_cursor_visible(visible: i32);
    fn horizon_uwp_set_cursor_position(x: f64, y: f64) -> i32;
    fn horizon_uwp_set_ime_allowed(allowed: i32);
}

pub struct Window { shared: Arc<Shared>, handle: usize, title: Mutex<String> }

impl Window {
    pub(crate) fn new(el: &ActiveEventLoop, attrs: window::WindowAttributes) -> Result<Self, error::OsError> {
        let handle = unsafe { super::event_loop::horizon_uwp_window_handle() } as usize;
        if handle == 0 { return Err(os_error!(std::io::Error::other("UWP CoreWindow is unavailable"))); }
        el.shared.created.store(true, Ordering::Release);
        Ok(Self { shared: el.shared.clone(), handle, title: Mutex::new(attrs.title) })
    }
    pub(crate) fn maybe_queue_on_main(&self, f: impl FnOnce(&Self) + Send + 'static) { f(self) }
    pub(crate) fn maybe_wait_on_main<R: Send>(&self, f: impl FnOnce(&Self) -> R + Send) -> R { f(self) }
    pub fn id(&self) -> WindowId { WindowId(1) }
    pub fn primary_monitor(&self) -> Option<MonitorHandle> { Some(MonitorHandle) }
    pub fn available_monitors(&self) -> VecDeque<MonitorHandle> { [MonitorHandle].into_iter().collect() }
    pub fn current_monitor(&self) -> Option<MonitorHandle> { Some(MonitorHandle) }
    pub fn scale_factor(&self) -> f64 { metrics().2 }
    pub fn request_redraw(&self) { self.shared.redraw.store(true, Ordering::Release); }
    pub fn pre_present_notify(&self) {}
    pub fn reset_dead_keys(&self) {}
    pub fn inner_position(&self) -> Result<PhysicalPosition<i32>, error::NotSupportedError> { Ok((0,0).into()) }
    pub fn outer_position(&self) -> Result<PhysicalPosition<i32>, error::NotSupportedError> { self.inner_position() }
    pub fn set_outer_position(&self, _: Position) {}
    pub fn inner_size(&self) -> PhysicalSize<u32> { let (w,h,_) = metrics(); (w,h).into() }
    pub fn request_inner_size(&self, _: Size) -> Option<PhysicalSize<u32>> { None }
    pub fn outer_size(&self) -> PhysicalSize<u32> { self.inner_size() }
    pub fn set_min_inner_size(&self, _: Option<Size>) {}
    pub fn set_max_inner_size(&self, _: Option<Size>) {}
    pub fn title(&self) -> String { self.title.lock().unwrap().clone() }
    pub fn set_title(&self, title: &str) { *self.title.lock().unwrap() = title.into(); }
    pub fn set_transparent(&self, _: bool) {}
    pub fn set_blur(&self, _: bool) {}
    pub fn set_visible(&self, _: bool) {}
    pub fn is_visible(&self) -> Option<bool> { Some(true) }
    pub fn resize_increments(&self) -> Option<PhysicalSize<u32>> { None }
    pub fn set_resize_increments(&self, _: Option<Size>) {}
    pub fn set_resizable(&self, _: bool) {}
    pub fn is_resizable(&self) -> bool { true }
    pub fn set_minimized(&self, _: bool) {}
    pub fn is_minimized(&self) -> Option<bool> { None }
    pub fn set_maximized(&self, _: bool) {}
    pub fn is_maximized(&self) -> bool { false }
    pub(crate) fn set_fullscreen(&self, _: Option<Fullscreen>) {}
    pub(crate) fn fullscreen(&self) -> Option<Fullscreen> { None }
    pub fn set_decorations(&self, _: bool) {}
    pub fn is_decorated(&self) -> bool { false }
    pub fn set_window_level(&self, _: window::WindowLevel) {}
    pub fn set_window_icon(&self, _: Option<crate::icon::Icon>) {}
    pub fn set_ime_cursor_area(&self, _: Position, _: Size) {}
    pub fn set_ime_allowed(&self, allowed: bool) { unsafe { horizon_uwp_set_ime_allowed(i32::from(allowed)); } }
    pub fn set_ime_purpose(&self, _: window::ImePurpose) {}
    pub fn focus_window(&self) {}
    pub fn request_user_attention(&self, _: Option<window::UserAttentionType>) {}
    pub fn set_cursor(&self, _: Cursor) {}
    pub fn set_cursor_position(&self, position: Position) -> Result<(), error::ExternalError> {
        let p = position.to_physical::<f64>(self.scale_factor());
        if unsafe { horizon_uwp_set_cursor_position(p.x, p.y) } != 0 { Ok(()) }
        else { Err(error::ExternalError::Ignored) }
    }
    pub fn set_cursor_grab(&self, mode: window::CursorGrabMode) -> Result<(), error::ExternalError> {
        match mode {
            window::CursorGrabMode::None | window::CursorGrabMode::Locked => Ok(()),
            window::CursorGrabMode::Confined => Err(error::ExternalError::NotSupported(error::NotSupportedError::new())),
        }
    }
    pub fn set_cursor_visible(&self, visible: bool) { unsafe { horizon_uwp_set_cursor_visible(visible as i32) }; }
    pub fn drag_window(&self) -> Result<(), error::ExternalError> { Err(error::ExternalError::NotSupported(error::NotSupportedError::new())) }
    pub fn drag_resize_window(&self, _: window::ResizeDirection) -> Result<(), error::ExternalError> { Err(error::ExternalError::NotSupported(error::NotSupportedError::new())) }
    pub fn show_window_menu(&self, _: Position) {}
    pub fn set_cursor_hittest(&self, _: bool) -> Result<(), error::ExternalError> { Err(error::ExternalError::NotSupported(error::NotSupportedError::new())) }
    #[cfg(feature = "rwh_06")]
    pub fn raw_window_handle_rwh_06(&self) -> Result<rwh_06::RawWindowHandle, rwh_06::HandleError> {
        let p = std::ptr::NonNull::new(self.handle as *mut _).ok_or(rwh_06::HandleError::Unavailable)?;
        Ok(rwh_06::WinRtWindowHandle::new(p).into())
    }
    #[cfg(feature = "rwh_06")]
    pub fn raw_display_handle_rwh_06(&self) -> Result<rwh_06::RawDisplayHandle, rwh_06::HandleError> { Ok(rwh_06::WindowsDisplayHandle::new().into()) }
    pub fn set_enabled_buttons(&self, _: window::WindowButtons) {}
    pub fn enabled_buttons(&self) -> window::WindowButtons { window::WindowButtons::empty() }
    pub fn theme(&self) -> Option<window::Theme> { None }
    pub fn has_focus(&self) -> bool { true }
    pub fn set_theme(&self, _: Option<window::Theme>) {}
    pub fn set_content_protected(&self, _: bool) {}
}

impl Drop for Window { fn drop(&mut self) { self.shared.created.store(false, Ordering::Release); } }
