// Compile for ARM32 against the Windows 10.0.15063 SDK to verify the layouts
// independently of the Rust bindings.
#include <winsock2.h>
#include <windows.h>
#include <delayloadhandler.h>
#include <stddef.h>

static_assert(sizeof(WSADATA) == 400, "WSADATA size");
static_assert(offsetof(WSADATA, lpVendorInfo) == 396, "WSADATA vendor offset");
static_assert(sizeof(SERVENT) == 16, "SERVENT size");
static_assert(offsetof(SERVENT, s_port) == 8, "SERVENT port offset");
static_assert(offsetof(SERVENT, s_proto) == 12, "SERVENT protocol offset");
static_assert(sizeof(MEMORY_BASIC_INFORMATION) == 28, "MEMORY_BASIC_INFORMATION size");
static_assert(offsetof(MEMORY_BASIC_INFORMATION, RegionSize) == 12, "region offset");
static_assert(sizeof(DELAYLOAD_INFO) == 36, "DELAYLOAD_INFO size");
static_assert(offsetof(DELAYLOAD_INFO, LastError) == 32, "DELAYLOAD_INFO error offset");
