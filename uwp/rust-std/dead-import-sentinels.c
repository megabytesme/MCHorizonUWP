/* Link-only sentinels for desktop APIs in unused Rust std object sections.
 * strict-link.ps1 refuses the result if any sentinel survives /OPT:REF. */
#include <intrin.h>

static void forbidden_api(void) { __fastfail(7); }
#define SENTINEL(name) void* __imp_##name = forbidden_api
SENTINEL(FreeEnvironmentStringsW);
SENTINEL(GetEnvironmentStringsW);
SENTINEL(InitializeProcThreadAttributeList);
SENTINEL(UpdateProcThreadAttribute);
SENTINEL(GetStdHandle);
SENTINEL(SetStdHandle);
SENTINEL(DeleteProcThreadAttributeList);
SENTINEL(lstrlenW);
SENTINEL(SetEnvironmentVariableW);
SENTINEL(CreateFileW);
SENTINEL(UnlockFile);
SENTINEL(CreateSymbolicLinkW);
SENTINEL(DeviceIoControl);
SENTINEL(CopyFileExW);
SENTINEL(GetConsoleMode);
SENTINEL(CreatePipe);
SENTINEL(GetSystemDirectoryW);
SENTINEL(GetEnvironmentVariableW);
SENTINEL(CreateProcessW);
SENTINEL(ExitProcess);
SENTINEL(GetModuleHandleA);
SENTINEL(GetModuleHandleW);
SENTINEL(GetModuleHandleExW);
