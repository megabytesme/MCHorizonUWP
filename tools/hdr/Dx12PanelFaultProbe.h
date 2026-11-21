// Included only in a staged x64 HDR panel fault diagnostic host.
extern "C" int __cdecl horizon_uwp_is_local_test();

extern "C" int32_t __cdecl horizon_uwp_hdr_panel_fault(uint32_t site) {
    if (!horizon_uwp_is_local_test()) return 0;
    static constexpr const wchar_t* requests[] = {
        L"hdr-panel-fresh-check.request", L"hdr-panel-reused-check.request",
        L"hdr-panel-fresh-set.request", L"hdr-panel-reused-set.request", L"hdr-panel-detach.request",
    };
    if (site >= _countof(requests)) return 0;
    try {
        const auto root = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        const std::wstring request = std::wstring(root->Data()) + L"\\" + requests[site];
        // A successful delete consumes this one-shot request even if multiple calls race.
        if (!DeleteFileW(request.c_str())) return 0;
        char detail[96];
        sprintf_s(detail, "HDR panel fault: consumed request site=%u", site);
        horizon_uwp_trace_shell_event(detail);
        return 1;
    } catch (...) {
        return 0;
    }
}
