#pragma once

#include <roapi.h>
#include <ppltasks.h>
#include <string_view>
#include <windows.storage.h>

namespace horizon_uwp {

// Call on the screenshot writer, after it closes the staged image.
inline HRESULT publish_screenshot(
    const wchar_t* staged_path, unsigned staged_length,
    const wchar_t* file_name, unsigned name_length) {
    if (!staged_path || !staged_length || !file_name || !name_length) return E_INVALIDARG;
    const std::wstring_view path_view(staged_path, staged_length);
    const std::wstring_view name_view(file_name, name_length);
    if (path_view.find(L'\0') != std::wstring_view::npos ||
        name_view.find(L'\0') != std::wstring_view::npos ||
        name_view.find_first_of(L"\\/:") != std::wstring_view::npos ||
        name_view.back() == L'.' || name_view.back() == L' ') return E_INVALIDARG;
    const HRESULT apartment = RoInitialize(RO_INIT_MULTITHREADED);
    if (FAILED(apartment)) return apartment;
    HRESULT status = S_OK;
    try {
        auto path = ref new Platform::String(staged_path, staged_length);
        auto name = ref new Platform::String(file_name, name_length);
        auto file = Concurrency::create_task(
            Windows::Storage::StorageFile::GetFileFromPathAsync(path)).get();
        Concurrency::create_task(file->RenameAsync(
            name, Windows::Storage::NameCollisionOption::FailIfExists)).get();
    } catch (Platform::Exception^ error) {
        status = error->HResult;
    } catch (...) {
        status = E_FAIL;
    }
    RoUninitialize();
    return status;
}

} // namespace horizon_uwp
