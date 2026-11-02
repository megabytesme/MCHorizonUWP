//! Owns a native renderer and its UWP host window on the UI thread.
#![cfg(target_vendor = "uwp")]

use horizon_uwp_d3d11::Renderer;
use std::{ffi::c_void, marker::PhantomData, rc::Rc};
use windows::{
    Win32::Foundation::E_INVALIDARG,
    core::{Error, IUnknown, Interface, Result},
};

unsafe extern "C" {
    /// The host returns a borrowed CoreWindow only from the owning UI thread.
    fn horizon_uwp_window_handle() -> *mut c_void;
}

/// Renderer that keeps the CoreWindow alive, and cannot cross threads.
pub struct NativeRenderer {
    // Rust drops fields in declaration order: the renderer must go first.
    renderer: Renderer,
    _window: IUnknown,
    _thread_bound: PhantomData<Rc<()>>,
}

impl NativeRenderer {
    /// Acquire the current UWP host window and create its D3D11 renderer.
    /// Returns an error if the window is unavailable or the caller is not on
    /// the owning UI thread; the host only exposes a handle in those cases.
    pub fn from_host_window() -> Result<Self> {
        let raw = unsafe { horizon_uwp_window_handle() };
        let window = unsafe { IUnknown::from_raw_borrowed(&raw) }
            .ok_or_else(|| Error::from_hresult(E_INVALIDARG))?
            .clone();
        // The host verified the owning thread, and `window` keeps its COM
        // interface alive for at least as long as `renderer`.
        let renderer = unsafe { Renderer::new(raw)? };
        Ok(Self {
            renderer,
            _window: window,
            _thread_bound: PhantomData,
        })
    }

    pub fn renderer(&self) -> &Renderer {
        &self.renderer
    }
    pub fn renderer_mut(&mut self) -> &mut Renderer {
        &mut self.renderer
    }
}
