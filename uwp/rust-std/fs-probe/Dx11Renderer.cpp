#include "Dx11Renderer.h"
#include <stdio.h>

extern "C" unsigned uwp_fs_probe(const wchar_t*, size_t);
extern "C" unsigned uwp_sync_probe();

namespace horizon_uwp {
HRESULT Dx11Renderer::Initialize(IUnknown*) noexcept {
    try {
        auto folder = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        auto log = Platform::String::Concat(folder, ref new Platform::String(L"\\runtime-probe.txt"));
        const unsigned fs = uwp_fs_probe(folder->Data(), folder->Length());
        const unsigned sync = uwp_sync_probe();
        char text[96] = {};
        const int size = sprintf_s(text, "filesystem: %u\nsynchronization: %u\n", fs, sync);
        HANDLE file = CreateFile2(log->Data(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
        if (file == INVALID_HANDLE_VALUE) return E_FAIL;
        DWORD written = 0;
        const BOOL ok = WriteFile(file, text, static_cast<DWORD>(size), &written, nullptr);
        CloseHandle(file);
        return ok && written == static_cast<DWORD>(size) && fs == 0 && sync == 0 ? S_OK : E_FAIL;
    } catch (Platform::Exception^) {
        return E_FAIL;
    }
}
}
