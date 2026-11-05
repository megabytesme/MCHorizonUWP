//! Compile-time checks against the Windows SDK's 32-bit ARM layouts.

#[cfg(all(target_arch = "arm", target_vendor = "uwp"))]
macro_rules! check {
    ($w:ident) => {
        const _: () = {
            use $w::Win32::Networking::WinSock::{SERVENT, WSADATA};
            use $w::Win32::System::Memory::MEMORY_BASIC_INFORMATION;
            use $w::Win32::System::WindowsProgramming::DELAYLOAD_INFO;
            assert!(core::mem::size_of::<WSADATA>() == 400);
            assert!(core::mem::offset_of!(WSADATA, lpVendorInfo) == 396);
            assert!(core::mem::size_of::<SERVENT>() == 16);
            assert!(core::mem::offset_of!(SERVENT, s_port) == 8);
            assert!(core::mem::offset_of!(SERVENT, s_proto) == 12);
            assert!(core::mem::size_of::<MEMORY_BASIC_INFORMATION>() == 28);
            assert!(core::mem::offset_of!(MEMORY_BASIC_INFORMATION, RegionSize) == 12);
            assert!(core::mem::size_of::<DELAYLOAD_INFO>() == 36);
            assert!(core::mem::offset_of!(DELAYLOAD_INFO, LastError) == 32);
        };
    };
}

#[cfg(all(target_arch = "arm", target_vendor = "uwp"))]
check!(w52);
#[cfg(all(target_arch = "arm", target_vendor = "uwp"))]
check!(w59);
#[cfg(all(target_arch = "arm", target_vendor = "uwp"))]
check!(w61);
