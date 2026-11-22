//! Entry points generated around a normal Horizon mod crate.
use std::{
    alloc::{GlobalAlloc, Layout},
    ffi::c_char,
    ptr,
    sync::OnceLock,
};

use mod_api::__private::native::{BuiltMod, NativeMod};

mod packaged_abi;
use packaged_abi::Allocator;

static HOST_ALLOCATOR: OnceLock<Allocator> = OnceLock::new();

struct HostAllocator;

// Rust values cross this boundary. Both DLLs must allocate and free on the game heap,
// including objects whose destructor is dispatched through a DLL-owned vtable.
unsafe impl GlobalAlloc for HostAllocator {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        if let Some(host) = HOST_ALLOCATOR.get() {
            unsafe { (host.alloc)(layout.size(), layout.align()) }
        } else {
            ptr::null_mut()
        }
    }

    unsafe fn dealloc(&self, pointer: *mut u8, layout: Layout) {
        if let Some(host) = HOST_ALLOCATOR.get() {
            unsafe { (host.dealloc)(pointer, layout.size(), layout.align()) }
        }
    }
}

#[global_allocator]
static ALLOCATOR: HostAllocator = HostAllocator;

#[unsafe(no_mangle)]
extern "C" fn horizon_uwp_native_init_v1(allocator: Allocator) -> u32 {
    u32::from(HOST_ALLOCATOR.set(allocator).is_ok())
}

#[unsafe(no_mangle)]
extern "C" fn horizon_native_build() -> *const c_char {
    concat!("__NATIVE_BUILD_IDENTITY__", "\0").as_ptr().cast()
}

const BUILT: &[BuiltMod] = &[/* BUILT_MOD */];

#[unsafe(no_mangle)]
fn horizon_native_mod_v1() -> &'static NativeMod {
    static MOD: OnceLock<NativeMod> = OnceLock::new();
    MOD.get_or_init(|| {
        NativeMod::new(
            || vec![mod_api::ModEntry::new(BUILT[0].id, super::hooks())],
            Vec::new,
            BUILT,
            Vec::new,
        )
    })
}
