//! Safe Rust facade around the UWP XAudio2 host callback.
#![cfg(target_vendor = "uwp")]
use std::{ffi::c_void, ptr::NonNull, sync::Mutex};
type Fill = Box<dyn FnMut(&mut [f32]) + Send>;
struct Callback {
    fill: Mutex<Fill>,
}
unsafe extern "C" {
    fn horizon_xaudio_open(
        callback: unsafe extern "C" fn(*mut c_void, *mut f32, usize),
        context: *mut c_void,
    ) -> *mut c_void;
    fn horizon_xaudio_close(stream: *mut c_void);
}
unsafe extern "C" fn fill(context: *mut c_void, data: *mut f32, len: usize) {
    if context.is_null() || data.is_null() || len > 16384 {
        return;
    }
    let output = unsafe { std::slice::from_raw_parts_mut(data, len) };
    let cb = unsafe { &*(context as *mut Callback) };
    if let Ok(mut f) = cb.fill.lock() {
        (f)(output)
    } else {
        output.fill(0.);
    }
}
pub struct Stream {
    native: NonNull<c_void>,
    callback: NonNull<Callback>,
}
impl Stream {
    pub fn open(fill: impl FnMut(&mut [f32]) + Send + 'static) -> Result<Self, String> {
        let callback = Box::new(Callback {
            fill: Mutex::new(Box::new(fill)),
        });
        let raw = Box::into_raw(callback);
        let native = unsafe { horizon_xaudio_open(fill_callback, raw.cast()) };
        if let Some(native) = NonNull::new(native) {
            Ok(Self {
                native,
                callback: NonNull::new(raw).unwrap(),
            })
        } else {
            unsafe { drop(Box::from_raw(raw)) };
            Err("XAudio2 output could not be opened".into())
        }
    }
}
unsafe extern "C" fn fill_callback(context: *mut c_void, data: *mut f32, len: usize) {
    unsafe { fill(context, data, len) }
}
impl Drop for Stream {
    fn drop(&mut self) {
        unsafe {
            horizon_xaudio_close(self.native.as_ptr());
            drop(Box::from_raw(self.callback.as_ptr()));
        }
    }
}
