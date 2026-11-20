//! Private bootstrap for a pinned Horizon Rust ABI across the two UWP DLLs.

#[repr(C)]
#[derive(Clone, Copy)]
pub(crate) struct Allocator {
    pub alloc: unsafe extern "C" fn(size: usize, align: usize) -> *mut u8,
    pub dealloc: unsafe extern "C" fn(pointer: *mut u8, size: usize, align: usize),
}
