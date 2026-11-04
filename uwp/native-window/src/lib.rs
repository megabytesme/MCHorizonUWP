//! Creates the CoreWindow swap chain on the UI thread, then transfers its
//! renderer once to the dedicated game thread.
#![cfg(target_vendor = "uwp")]

use horizon_uwp_d3d11::Renderer;
use std::sync::Mutex;
use std::{ffi::c_void, marker::PhantomData, rc::Rc};
use windows::{
    Win32::Foundation::E_INVALIDARG,
    core::{Error, IUnknown, Interface, Result},
};

unsafe extern "C" {
    /// Returns a borrowed CoreWindow on the UI thread, or an agile proxy on
    /// the game thread. Surface creation still uses the UI-thread reference.
    fn horizon_uwp_window_handle() -> *mut c_void;
    fn horizon_uwp_get_surface_metrics(
        width: *mut u32,
        height: *mut u32,
        scale_x: *mut f64,
        scale_y: *mut f64,
    );
    fn horizon_uwp_attach_game_swapchain(swap_chain: *mut c_void) -> i32;
}

static PREPARED_RENDERER: Mutex<Option<usize>> = Mutex::new(None);

/// Renderer whose swap chain was created on the owning CoreWindow thread.
pub struct NativeRenderer {
    renderer: Renderer,
    _thread_bound: PhantomData<Rc<()>>,
}

impl NativeRenderer {
    /// Create the base D3D11 renderer while called from the CoreWindow UI
    /// thread. The game thread consumes it after XAML resumes its dispatcher.
    pub fn prepare_on_ui_thread() -> windows::core::Result<()> {
        let mut slot = PREPARED_RENDERER
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        if slot.is_some() {
            return Err(Error::from_hresult(E_INVALIDARG));
        }
        let mut width = 0;
        let mut height = 0;
        let (mut scale_x, mut scale_y) = (1.0, 1.0);
        unsafe {
            horizon_uwp_get_surface_metrics(&mut width, &mut height, &mut scale_x, &mut scale_y)
        };
        if width == 0
            || height == 0
            || !scale_x.is_finite()
            || !scale_y.is_finite()
            || scale_x <= 0.0
            || scale_y <= 0.0
        {
            return Err(Error::from_hresult(E_INVALIDARG));
        }
        let renderer =
            Renderer::new_for_composition(width, height, scale_x as f32, scale_y as f32)?;
        let attach_result = unsafe { horizon_uwp_attach_game_swapchain(renderer.swap_chain_ptr()) };
        if attach_result < 0 {
            return Err(Error::from_hresult(windows::core::HRESULT(attach_result)));
        }
        log::info!(
            "Horizon UWP composition surface: {}x{} physical pixels; inverse panel scale {:.3}x{:.3}",
            width,
            height,
            scale_x,
            scale_y
        );
        *slot = Some(Box::into_raw(Box::new(renderer)) as usize);
        Ok(())
    }

    /// Consume the prepared renderer on the game thread. This is a unique
    /// transfer: after the slot is taken, only this thread uses the D3D11
    /// immediate context. If no UI-thread preparation was requested, this
    /// keeps the legacy same-thread path for native probes.
    pub fn from_host_window() -> Result<Self> {
        let prepared = PREPARED_RENDERER
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .take();
        let renderer = if let Some(pointer) = prepared {
            // SAFETY: prepare_on_ui_thread allocated this unique Renderer and
            // the mutex transfers its pointer exactly once to the game thread.
            unsafe { *Box::from_raw(pointer as *mut Renderer) }
        } else {
            let raw = unsafe { horizon_uwp_window_handle() };
            let window = unsafe { IUnknown::from_raw_borrowed(&raw) }
                .ok_or_else(|| Error::from_hresult(E_INVALIDARG))?
                .clone();
            let renderer = unsafe { Renderer::new(raw)? };
            drop(window);
            renderer
        };
        Ok(Self {
            renderer,
            _thread_bound: PhantomData,
        })
    }

    pub fn renderer(&self) -> &Renderer {
        &self.renderer
    }
    pub fn renderer_mut(&mut self) -> &mut Renderer {
        &mut self.renderer
    }

    pub fn resize(&mut self, width: u32, height: u32) -> Result<()> {
        if width == 0 || height == 0 {
            return Ok(());
        }
        if self.renderer.swap_chain_size()? != [width, height] {
            self.renderer.resize_to(width, height)?;
        }
        let mut current_width = 0;
        let mut current_height = 0;
        let (mut scale_x, mut scale_y) = (1.0, 1.0);
        unsafe {
            horizon_uwp_get_surface_metrics(
                &mut current_width,
                &mut current_height,
                &mut scale_x,
                &mut scale_y,
            )
        };
        if current_width > 0
            && current_height > 0
            && scale_x.is_finite()
            && scale_y.is_finite()
            && scale_x > 0.0
            && scale_y > 0.0
        {
            self.renderer
                .set_composition_scale(scale_x as f32, scale_y as f32)?;
        }
        Ok(())
    }
}
