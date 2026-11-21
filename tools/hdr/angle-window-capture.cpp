#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <DirectXPackedVector.h>
#include <shobjidl.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <chrono>
#include <cstdio>
#include <thread>

namespace capture = winrt::Windows::Graphics::Capture;
namespace directx = winrt::Windows::Graphics::DirectX;
namespace d3d = directx::Direct3D11;

namespace {
struct WindowSearch {
    DWORD pid;
    HWND result = nullptr;
    HWND core_window = nullptr;
};

BOOL CALLBACK find_probe_child(HWND window, LPARAM data) {
    auto& search = *reinterpret_cast<WindowSearch*>(data);
    DWORD owner = 0;
    GetWindowThreadProcessId(window, &owner);
    if (owner != search.pid) return TRUE;
    const HWND root = GetAncestor(window, GA_ROOT);
    wchar_t window_class[128]{};
    GetClassNameW(root, window_class, ARRAYSIZE(window_class));
    if (std::wcscmp(window_class, L"Windows.UI.Core.CoreWindow") == 0) {
        search.core_window = root;
        return TRUE; // The CoreWindow can appear before its desktop frame is attached.
    }
    search.result = root;
    return FALSE;
}

BOOL CALLBACK find_probe_window(HWND window, LPARAM data) {
    auto& search = *reinterpret_cast<WindowSearch*>(data);
    if (!IsWindowVisible(window) || IsIconic(window)) return TRUE;
    if (!find_probe_child(window, data)) return FALSE;
    EnumChildWindows(window, find_probe_child, data);
    return search.result ? FALSE : TRUE;
}

bool check_frame(ID3D11Device* device, ID3D11DeviceContext* context,
    const capture::Direct3D11CaptureFrame& frame) {
    const auto access = frame.Surface().as<Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    winrt::com_ptr<ID3D11Texture2D> texture;
    winrt::check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), texture.put_void()));
    D3D11_TEXTURE2D_DESC desc{};
    texture->GetDesc(&desc);
    if (desc.Format != DXGI_FORMAT_R16G16B16A16_FLOAT) {
        std::printf("unexpected_capture_format=%u\n", desc.Format);
        return false;
    }

    desc.Usage = D3D11_USAGE_STAGING;
    desc.BindFlags = 0;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    desc.MiscFlags = 0;
    winrt::com_ptr<ID3D11Texture2D> staging;
    winrt::check_hresult(device->CreateTexture2D(&desc, nullptr, staging.put()));
    context->CopyResource(staging.get(), texture.get());
    D3D11_MAPPED_SUBRESOURCE mapped{};
    winrt::check_hresult(context->Map(staging.get(), 0, D3D11_MAP_READ, 0, &mapped));

    const auto size = frame.ContentSize();
    unsigned matching = 0;
    float center[4]{};
    for (int y = 0; y < size.Height && static_cast<unsigned>(y) < desc.Height; ++y) {
        const auto row = reinterpret_cast<const uint16_t*>(static_cast<const std::byte*>(mapped.pData) + y * mapped.RowPitch);
        for (int x = 0; x < size.Width && static_cast<unsigned>(x) < desc.Width; ++x) {
            const auto pixel = row + x * 4;
            // These are the exact binary16 forms of the probe's extended scRGB pattern.
            if (pixel[0] == 0xb000 && pixel[1] == 0x3800 && pixel[2] == 0x4400 && pixel[3] == 0x3c00) {
                ++matching;
            }
            if (x == size.Width / 2 && y == size.Height / 2) {
                for (int channel = 0; channel < 4; ++channel) {
                    center[channel] = DirectX::PackedVector::XMConvertHalfToFloat(pixel[channel]);
                }
            }
        }
    }
    context->Unmap(staging.get(), 0);
    std::printf("capture_format=%u content=%dx%d exact_probe_pixels=%u center=%f/%f/%f/%f\n",
        desc.Format, size.Width, size.Height, matching, center[0], center[1], center[2], center[3]);
    return matching >= 1024;
}
}

int wmain(int argc, wchar_t** argv) try {
    if (argc != 2) {
        std::fprintf(stderr, "Usage: angle-window-capture.exe <probe AUMID>\n");
        return 2;
    }
    winrt::init_apartment();
    std::printf("wgc_step=activate\n");
    auto launcher = winrt::create_instance<IApplicationActivationManager>(CLSID_ApplicationActivationManager, CLSCTX_LOCAL_SERVER);
    WindowSearch search{};
    winrt::check_hresult(launcher->ActivateApplication(argv[1], nullptr, AO_NONE, &search.pid));
    std::printf("probe_pid=%lu\n", search.pid);
    for (int i = 0; i < 80 && !search.result; ++i) {
        EnumWindows(find_probe_window, reinterpret_cast<LPARAM>(&search));
        if (!search.result) std::this_thread::sleep_for(std::chrono::milliseconds(50));
    }
    if (!search.result) search.result = search.core_window;
    if (!search.result) {
        std::fprintf(stderr, "Probe window did not become visible.\n");
        return 1;
    }

    wchar_t window_class[128]{};
    GetClassNameW(search.result, window_class, ARRAYSIZE(window_class));
    std::printf("capture_hwnd=%p class=%ls\n", search.result, window_class);
    std::printf("wgc_step=create_item\n");
    auto factory = winrt::get_activation_factory<capture::GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
    capture::GraphicsCaptureItem item{ nullptr };
    winrt::check_hresult(factory->CreateForWindow(search.result, winrt::guid_of<capture::IGraphicsCaptureItem>(),
        winrt::put_abi(item)));
    const auto size = item.Size();
    std::printf("capture_item_size=%dx%d\n", size.Width, size.Height);
    if (size.Width <= 0 || size.Height <= 0) {
        std::fprintf(stderr, "Capture window has no available composition surface.\n");
        return 1;
    }

    std::printf("wgc_step=create_d3d_device\n");
    winrt::com_ptr<ID3D11Device> device;
    winrt::com_ptr<ID3D11DeviceContext> context;
    winrt::check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, device.put(), nullptr, context.put()));
    winrt::com_ptr<::IInspectable> surface_device;
    winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(device.as<IDXGIDevice>().get(), surface_device.put()));
    std::printf("wgc_step=create_pool\n");
    auto pool = capture::Direct3D11CaptureFramePool::CreateFreeThreaded(surface_device.as<d3d::IDirect3DDevice>(),
        directx::DirectXPixelFormat::R16G16B16A16Float, 2, size);
    auto session = pool.CreateCaptureSession(item);
    session.IsCursorCaptureEnabled(false);
    std::printf("wgc_step=start_capture\n");
    session.StartCapture();
    for (int i = 0; i < 80; ++i) {
        if (auto frame = pool.TryGetNextFrame(); frame && check_frame(device.get(), context.get(), frame)) {
            std::printf("wgc_probe_match=true\n");
            return 0;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }
    std::printf("wgc_probe_match=false\n");
    return 1;
} catch (const winrt::hresult_error& error) {
    std::fwprintf(stderr, L"WGC failed 0x%08x: %ls\n", static_cast<unsigned>(error.code()), error.message().c_str());
    return 1;
}
