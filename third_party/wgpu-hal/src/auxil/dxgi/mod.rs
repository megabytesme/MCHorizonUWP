// Every module here is gated behind `dx12` except `hdr` (the
// `DXGI_OUTPUT_DESC1` -> `DisplayHdrInfo` mapping), which the
// Vulkan-on-Windows backend also uses.
#[cfg(dx12)]
pub mod conv;
#[cfg(dx12)]
#[cfg(not(all(target_vendor = "uwp", target_arch = "arm")))]
pub mod exception;

// ARM32 UWP bindings do not expose the desktop vectored-debugging APIs.
// Rendering is unaffected; only capture of driver debug messages is omitted.
#[cfg(all(dx12, target_vendor = "uwp", target_arch = "arm"))]
pub mod exception {
    pub fn register_exception_handler() {}
    pub fn unregister_exception_handler() {}
}
#[cfg(dx12)]
pub mod factory;
#[cfg(not(target_vendor = "uwp"))]
pub mod hdr;

// UWP presents through CoreWindow/SwapChainPanel, not an HWND. Avoid loading
// desktop-only monitor APIs just to obtain optional HDR metadata.
#[cfg(target_vendor = "uwp")]
pub mod hdr {
    pub struct DxgiHdrSource;
    impl DxgiHdrSource {
        pub fn new(_: windows::Win32::Foundation::HWND) -> Self {
            Self
        }
        pub fn display_hdr_info(&self) -> Option<wgt::DisplayHdrInfo> {
            None
        }
    }
}
#[cfg(dx12)]
pub mod name;
#[cfg(dx12)]
pub mod result;
#[cfg(dx12)]
pub mod time;
