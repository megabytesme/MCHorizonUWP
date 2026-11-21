// Included only in the separately signed recovery-test XAML host.
#include <d3d12.h>
#include <dxgi1_6.h>

extern "C" int __cdecl horizon_uwp_is_local_test();

static void RunDx12RecoveryProbe(IDXGISwapChain3* chain, int32_t space, int32_t status) {
    using Microsoft::WRL::ComPtr;
    using Windows::Foundation::TimeSpan;
    using Windows::System::Threading::ThreadPoolTimer;
    using Windows::System::Threading::TimerElapsedHandler;

    static std::atomic<bool> started{false};
    if ((space != DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709 &&
        space != DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709) || FAILED(status) ||
        started.load() || !horizon_uwp_is_local_test()) return;

    try {
        const auto root = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        const std::wstring request = std::wstring(root->Data()) + L"\\hdr-dx12-recovery.request";
        HANDLE file = CreateFile2(request.c_str(), GENERIC_READ, FILE_SHARE_READ, OPEN_EXISTING, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        CloseHandle(file);
        if (started.exchange(true)) return;
        DeleteFileW(request.c_str());

        ComPtr<ID3D12Device5> device;
        HRESULT hr = chain->GetDevice(IID_PPV_ARGS(&device));
        if (FAILED(hr)) {
            ComPtr<ID3D12CommandQueue> queue;
            hr = chain->GetDevice(IID_PPV_ARGS(&queue));
            if (SUCCEEDED(hr)) hr = queue->GetDevice(IID_PPV_ARGS(&device));
        }
        if (FAILED(hr)) {
            horizon_uwp_trace_shell_event("HDR QA could not acquire ID3D12Device5");
            return;
        }

        ComPtr<IDXGIFactory4> factory;
        ComPtr<IDXGIAdapter3> adapter;
        if (SUCCEEDED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) {
            factory->EnumAdapterByLuid(device->GetAdapterLuid(), IID_PPV_ARGS(&adapter));
        }
        DXGI_QUERY_VIDEO_MEMORY_INFO before{};
        ComPtr<ID3D12Resource> pressure;
        constexpr UINT64 bytes = 256ull * 1024 * 1024;
        if (adapter && SUCCEEDED(adapter->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &before)) &&
            before.Budget >= before.CurrentUsage && before.Budget - before.CurrentUsage >= bytes * 2) {
            D3D12_HEAP_PROPERTIES heap{};
            heap.Type = D3D12_HEAP_TYPE_DEFAULT;
            heap.CreationNodeMask = 1;
            heap.VisibleNodeMask = 1;
            D3D12_RESOURCE_DESC desc{};
            desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
            desc.Width = bytes;
            desc.Height = 1;
            desc.DepthOrArraySize = 1;
            desc.MipLevels = 1;
            desc.SampleDesc.Count = 1;
            desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
            hr = device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
                D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&pressure));
            char detail[208];
            sprintf_s(detail, "HDR QA pressure allocation: hr=0x%08X bytes=%llu usage_before=%llu budget=%llu",
                static_cast<unsigned>(hr), bytes, before.CurrentUsage, before.Budget);
            horizon_uwp_trace_shell_event(detail);
        } else {
            horizon_uwp_trace_shell_event("HDR QA pressure allocation skipped: insufficient or unknown headroom");
        }

        TimeSpan delay{};
        delay.Duration = 2 * 10'000'000;
        ThreadPoolTimer::CreateTimer(ref new TimerElapsedHandler(
            [device, adapter, pressure](ThreadPoolTimer^) mutable {
                if (adapter && pressure) {
                    DXGI_QUERY_VIDEO_MEMORY_INFO held{};
                    if (SUCCEEDED(adapter->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &held))) {
                        char detail[136];
                        sprintf_s(detail, "HDR QA pressure held: usage=%llu budget=%llu", held.CurrentUsage, held.Budget);
                        horizon_uwp_trace_shell_event(detail);
                    }
                }
                pressure.Reset();
                horizon_uwp_trace_shell_event("HDR QA removing only the isolated game's D3D12 device");
                device->RemoveDevice();
                char detail[96];
                sprintf_s(detail, "HDR QA removed reason=0x%08X",
                    static_cast<unsigned>(device->GetDeviceRemovedReason()));
                horizon_uwp_trace_shell_event(detail);
            }), delay);
    } catch (...) {
        horizon_uwp_trace_shell_event("HDR QA recovery probe could not be scheduled");
    }
}
