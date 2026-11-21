// Standalone desktop DXGI diagnostic. Uses real adapters and unattached
// composition chains, so it does not measure a XAML view or panel luminance.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include <cstdio>
#include <vector>

using Microsoft::WRL::ComPtr;

template<typename T>
LONG QueryTarget(T& request, DISPLAYCONFIG_DEVICE_INFO_TYPE type, const DISPLAYCONFIG_PATH_TARGET_INFO& target)
{
    request.header = {type, sizeof(request), target.adapterId, target.id};
    return DisplayConfigGetDeviceInfo(&request.header);
}

void ProbeActiveTargets()
{
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;
    LONG status;
    std::vector<DISPLAYCONFIG_PATH_INFO> paths;
    std::vector<DISPLAYCONFIG_MODE_INFO> modes;
    do {
        status = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount);
        if (status != ERROR_SUCCESS) break;
        paths.resize(pathCount);
        modes.resize(modeCount);
        status = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr);
    } while (status == ERROR_INSUFFICIENT_BUFFER);

    std::printf("activeTargetsQuery=%ld\n", status);
    if (status != ERROR_SUCCESS) return;
    for (UINT32 i = 0; i < pathCount; ++i) {
        auto& target = paths[i].targetInfo;
        DISPLAYCONFIG_TARGET_DEVICE_NAME name{};
        DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 advanced{};
        DISPLAYCONFIG_SDR_WHITE_LEVEL sdrWhite{};
        const auto nameStatus = QueryTarget(name, DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, target);
        const auto colorStatus = QueryTarget(advanced, DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2, target);
        const auto whiteStatus = QueryTarget(sdrWhite, DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL, target);
        std::printf("target=%u nameStatus=%ld name=\"%ls\" colorStatus=%ld hdrSupported=%u hdrUserEnabled=%u "
            "advancedColorActive=%u activeColorMode=%u bitsPerChannel=%u whiteStatus=%ld sdrWhiteNits=%.3f\n",
            target.id, nameStatus, name.monitorFriendlyDeviceName, colorStatus, advanced.highDynamicRangeSupported,
            advanced.highDynamicRangeUserEnabled, advanced.advancedColorActive, advanced.activeColorMode,
            advanced.bitsPerColorChannel, whiteStatus, sdrWhite.SDRWhiteLevel * 0.08);
    }
}

struct FormatCase {
    const char* name;
    DXGI_FORMAT format;
    DXGI_COLOR_SPACE_TYPE space;
};

constexpr FormatCase formats[] = {
    {"BGRA8/sRGB", DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709},
    {"FP16/scRGB", DXGI_FORMAT_R16G16B16A16_FLOAT, DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709},
    {"RGB10/PQ", DXGI_FORMAT_R10G10B10A2_UNORM, DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020},
};

void ProbeChain(IDXGIFactory2* factory, IUnknown* deviceOrQueue, const char* backend, const FormatCase& test,
    DXGI_ALPHA_MODE alpha, ID3D11Device* d3d11 = nullptr, ID3D11DeviceContext* context = nullptr)
{
    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width = 64;
    desc.Height = 64;
    desc.Format = test.format;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.Scaling = DXGI_SCALING_STRETCH;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.AlphaMode = alpha;
    ComPtr<IDXGISwapChain1> chain;
    const auto create = factory->CreateSwapChainForComposition(deviceOrQueue, &desc, nullptr, &chain);
    std::printf("%s format=%s alpha=%u create=0x%08lX", backend, test.name, alpha, create);
    if (FAILED(create)) {
        std::puts("");
        return;
    }

    DXGI_SWAP_CHAIN_DESC1 actual{};
    const auto getDescription = chain->GetDesc1(&actual);
    ComPtr<IDXGISwapChain3> colorChain;
    const auto colorInterface = chain.As(&colorChain);
    std::printf(" desc=0x%08lX actualFormat=%u actualAlpha=%u colorInterface=0x%08lX",
        getDescription, actual.Format, actual.AlphaMode, colorInterface);
    if (SUCCEEDED(colorInterface)) {
        UINT support = 0;
        const auto check = colorChain->CheckColorSpaceSupport(test.space, &support);
        const auto set = colorChain->SetColorSpace1(test.space);
        std::printf(" colorSpace=%u check=0x%08lX support=0x%X set=0x%08lX", test.space, check, support, set);
    }
    ComPtr<IDXGISwapChain4> metadataChain;
    std::printf(" metadataInterface=0x%08lX", chain.As(&metadataChain));
    if (d3d11) {
        ComPtr<ID3D11Texture2D> buffer;
        const auto getBuffer = chain->GetBuffer(0, IID_PPV_ARGS(&buffer));
        ComPtr<ID3D11RenderTargetView> view;
        const auto createView = buffer ? d3d11->CreateRenderTargetView(buffer.Get(), nullptr, &view) : getBuffer;
        std::printf(" buffer=0x%08lX rtv=0x%08lX", getBuffer, createView);
        if (view && test.format == DXGI_FORMAT_R16G16B16A16_FLOAT) {
            const float rgba[] = {-0.25f, 1.0f, 4.0f, 1.0f};
            context->ClearRenderTargetView(view.Get(), rgba);
            D3D11_TEXTURE2D_DESC stagingDesc{};
            buffer->GetDesc(&stagingDesc);
            stagingDesc.Usage = D3D11_USAGE_STAGING;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            stagingDesc.MiscFlags = 0;
            ComPtr<ID3D11Texture2D> staging;
            const auto stagingResult = d3d11->CreateTexture2D(&stagingDesc, nullptr, &staging);
            std::printf(" staging=0x%08lX", stagingResult);
            if (staging) {
                context->CopyResource(staging.Get(), buffer.Get());
                D3D11_MAPPED_SUBRESOURCE mapped{};
                const auto map = context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped);
                std::printf(" map=0x%08lX", map);
                if (SUCCEEDED(map)) {
                    const auto* pixel = static_cast<const UINT16*>(mapped.pData);
                    std::printf(" halfPixel=%04X,%04X,%04X,%04X", pixel[0], pixel[1], pixel[2], pixel[3]);
                    context->Unmap(staging.Get(), 0);
                }
            }
        }
    }
    std::puts("");
}

void ProbeD3D11(IDXGIFactory2* factory, IDXGIAdapter1* adapter)
{
    constexpr D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_9_1};
    for (const auto level : levels) {
        ComPtr<ID3D11Device> device;
        ComPtr<ID3D11DeviceContext> context;
        const auto status = D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, &level, 1, D3D11_SDK_VERSION, &device, nullptr, &context);
        std::printf("D3D11 level=0x%04X device=0x%08lX\n", level, status);
        if (!device) continue;
        const char* backend = level == D3D_FEATURE_LEVEL_9_1 ? "D3D11-FL9_1" : "D3D11-FL11_1";
        for (const auto& test : formats) {
            UINT support = 0;
            const auto query = device->CheckFormatSupport(test.format, &support);
            std::printf("%s format=%s query=0x%08lX flags=0x%X texture2d=%u sample=%u rtv=%u blend=%u display=%u\n",
                backend, test.name, query, support, !!(support & D3D11_FORMAT_SUPPORT_TEXTURE2D),
                !!(support & D3D11_FORMAT_SUPPORT_SHADER_SAMPLE), !!(support & D3D11_FORMAT_SUPPORT_RENDER_TARGET),
                !!(support & D3D11_FORMAT_SUPPORT_BLENDABLE), !!(support & D3D11_FORMAT_SUPPORT_DISPLAY));
            ProbeChain(factory, device.Get(), backend, test, DXGI_ALPHA_MODE_IGNORE, device.Get(), context.Get());
        }
    }
}

void ProbeD3D12(IDXGIFactory2* factory, IDXGIAdapter1* adapter)
{
    ComPtr<ID3D12Device> device;
    const auto status = D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device));
    std::printf("D3D12 device=0x%08lX\n", status);
    if (!device) return;
    D3D12_COMMAND_QUEUE_DESC desc{};
    ComPtr<ID3D12CommandQueue> queue;
    const auto queueStatus = device->CreateCommandQueue(&desc, IID_PPV_ARGS(&queue));
    std::printf("D3D12 queue=0x%08lX\n", queueStatus);
    if (!queue) return;
    for (const auto& test : formats) {
        ProbeChain(factory, queue.Get(), "D3D12", test, DXGI_ALPHA_MODE_IGNORE);
        ProbeChain(factory, queue.Get(), "D3D12", test, DXGI_ALPHA_MODE_PREMULTIPLIED);
    }
}

int main()
{
    ProbeActiveTargets();
    ComPtr<IDXGIFactory2> factory;
    const auto create = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if (FAILED(create)) {
        std::fprintf(stderr, "CreateDXGIFactory1 failed: 0x%08lX\n", create);
        return 1;
    }
    for (UINT index = 0; ; ++index) {
        ComPtr<IDXGIAdapter1> adapter;
        const auto next = factory->EnumAdapters1(index, &adapter);
        if (next == DXGI_ERROR_NOT_FOUND) break;
        if (FAILED(next)) return 1;
        DXGI_ADAPTER_DESC1 desc{};
        if (FAILED(adapter->GetDesc1(&desc)) || (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE)) continue;
        std::printf("adapter=\"%ls\" vendor=0x%04X device=0x%04X\n", desc.Description, desc.VendorId, desc.DeviceId);
        for (UINT outputIndex = 0; ; ++outputIndex) {
            ComPtr<IDXGIOutput> output;
            if (FAILED(adapter->EnumOutputs(outputIndex, &output))) break;
            ComPtr<IDXGIOutput6> output6;
            DXGI_OUTPUT_DESC1 mode{};
            if (SUCCEEDED(output.As(&output6)) && SUCCEEDED(output6->GetDesc1(&mode))) {
                std::printf("output=\"%ls\" attached=%u bitsPerChannel=%u colorSpace=%u min=%.4f max=%.3f maxFull=%.3f "
                    "red=%.4f,%.4f green=%.4f,%.4f blue=%.4f,%.4f white=%.4f,%.4f\n",
                    mode.DeviceName, mode.AttachedToDesktop, mode.BitsPerColor, mode.ColorSpace,
                    mode.MinLuminance, mode.MaxLuminance, mode.MaxFullFrameLuminance, mode.RedPrimary[0],
                    mode.RedPrimary[1], mode.GreenPrimary[0], mode.GreenPrimary[1], mode.BluePrimary[0],
                    mode.BluePrimary[1], mode.WhitePoint[0], mode.WhitePoint[1]);
            }
        }
        ProbeD3D11(factory.Get(), adapter.Get());
        ProbeD3D12(factory.Get(), adapter.Get());
    }
}
