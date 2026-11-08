unsigned long _tls_index = 0;
int _fltused = 0x9875;

/* Rust's Windows TLS runtime uses the process TLS index when initializing
 * thread-local values. The UWP bridge is linked with rust-lld directly rather
 * than the MSVC linker startup objects, so provide the PE TLS directory and
 * section sentinels explicitly. */
#pragma section(".tls$", long, read, write)
#pragma section(".tls$ZZZ", long, read, write)
#pragma section(".CRT$XLA", long, read)
#pragma section(".CRT$XLZ", long, read)
#pragma section(".tls", long, read, write)

#if __SIZEOF_POINTER__ == 8
typedef unsigned long long horizon_tls_uintptr_t;
#else
typedef unsigned long horizon_tls_uintptr_t;
#endif

typedef struct HorizonImageTlsDirectory {
    horizon_tls_uintptr_t StartAddressOfRawData;
    horizon_tls_uintptr_t EndAddressOfRawData;
    horizon_tls_uintptr_t AddressOfIndex;
    horizon_tls_uintptr_t AddressOfCallBacks;
    unsigned long SizeOfZeroFill;
    unsigned long Characteristics;
} HorizonImageTlsDirectory;

extern unsigned long _tls_index;
__declspec(allocate(".tls$")) char _tls_start = 0;
__declspec(allocate(".tls$ZZZ")) char _tls_end = 0;
__declspec(allocate(".CRT$XLA")) void *__xl_a = 0;
__declspec(allocate(".CRT$XLZ")) void *__xl_z = 0;
__declspec(allocate(".tls")) const HorizonImageTlsDirectory _tls_used = {
    (horizon_tls_uintptr_t)&_tls_start,
    (horizon_tls_uintptr_t)&_tls_end,
    (horizon_tls_uintptr_t)&_tls_index,
    (horizon_tls_uintptr_t)(&__xl_a + 1),
    0,
    0
};

typedef void (__cdecl *HorizonAtExitCallback)(void);
__declspec(dllimport) int __cdecl _crt_atexit(HorizonAtExitCallback callback);

int __cdecl atexit(HorizonAtExitCallback callback)
{
    return _crt_atexit(callback);
}
