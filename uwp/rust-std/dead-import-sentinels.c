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
SENTINEL(GetEnvironmentVariableA);
SENTINEL(VirtualAlloc);
SENTINEL(VirtualProtect);
SENTINEL(ShellExecuteExW);
SENTINEL(CoInitialize);
SENTINEL(ILCreateFromPathW);
SENTINEL(SHOpenFolderAndSelectItems);
SENTINEL(ILFree);
SENTINEL(EmptyClipboard);
SENTINEL(OpenClipboard);
SENTINEL(IsClipboardFormatAvailable);
SENTINEL(ImpersonateAnonymousToken);
SENTINEL(CloseClipboard);
SENTINEL(RevertToSelf);
SENTINEL(GetClipboardData);
SENTINEL(SetClipboardData);
SENTINEL(GetClipboardFormatNameW);
SENTINEL(DragQueryFileW);
SENTINEL(RegisterClipboardFormatW);
SENTINEL(EnumClipboardFormats);
SENTINEL(CountClipboardFormats);
SENTINEL(NotifyIpInterfaceChange);

/* On x86 Windows imports use stdcall-decorated names. These are still
 * link-only sentinels: strict-link rejects any that survive /OPT:REF. */
#if defined(_M_IX86)
#pragma comment(linker, "/alternatename:__imp__lstrlenW@4=___imp_lstrlenW")
#pragma comment(linker, "/alternatename:__imp__CreatePipe@16=___imp_CreatePipe")
#pragma comment(linker, "/alternatename:__imp__NotifyIpInterfaceChange@20=___imp_NotifyIpInterfaceChange")
#endif
