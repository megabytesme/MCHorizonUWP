#include "Dx11Renderer.h"

using Microsoft::WRL::ComPtr;

namespace horizon_uwp {

HRESULT Dx11Renderer::Initialize(IUnknown* coreWindow) noexcept {
    if (!coreWindow) return E_INVALIDARG;

    renderTarget_.Reset();
    swapChain_.Reset();
    context_.Reset();
    device_.Reset();
    window_ = coreWindow;

    // Request lower feature levels as well as desktop levels so that the
    // selected level can be inspected on older mobile hardware.
    const D3D_FEATURE_LEVEL levels[] = {
        D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0,
        D3D_FEATURE_LEVEL_9_3, D3D_FEATURE_LEVEL_9_2,
        D3D_FEATURE_LEVEL_9_1,
    };
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, ARRAYSIZE(levels),
        D3D11_SDK_VERSION, &device_, &featureLevel_, &context_);
    if (FAILED(hr)) return hr;

    ComPtr<IDXGIDevice> dxgiDevice;
    ComPtr<IDXGIAdapter> adapter;
    ComPtr<IDXGIFactory2> factory;
    if (FAILED(hr = device_.As(&dxgiDevice))) return hr;
    if (FAILED(hr = dxgiDevice->GetAdapter(&adapter))) return hr;
    if (FAILED(hr = adapter->GetParent(IID_PPV_ARGS(&factory)))) return hr;

    DXGI_SWAP_CHAIN_DESC1 desc = {};
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.Scaling = DXGI_SCALING_NONE;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    if (FAILED(hr = factory->CreateSwapChainForCoreWindow(device_.Get(),
            window_.Get(), &desc, nullptr, &swapChain_))) return hr;
    return CreateRenderTarget();
}

HRESULT Dx11Renderer::CreateRenderTarget() noexcept {
    ComPtr<ID3D11Texture2D> backBuffer;
    HRESULT hr = swapChain_->GetBuffer(0, IID_PPV_ARGS(&backBuffer));
    if (FAILED(hr)) return hr;
    return device_->CreateRenderTargetView(backBuffer.Get(), nullptr, &renderTarget_);
}

HRESULT Dx11Renderer::Resize() noexcept {
    if (!swapChain_.Get() || !context_.Get()) return E_UNEXPECTED;
    context_->OMSetRenderTargets(0, nullptr, nullptr);
    renderTarget_.Reset();
    HRESULT hr = swapChain_->ResizeBuffers(2, 0, 0, DXGI_FORMAT_B8G8R8A8_UNORM, 0);
    if (FAILED(hr)) return hr;
    return CreateRenderTarget();
}

HRESULT Dx11Renderer::Render() noexcept {
    if (!context_.Get() || !renderTarget_.Get() || !swapChain_.Get()) return E_UNEXPECTED;
    const float clear[] = { 0.04f, 0.18f, 0.28f, 1.0f };
    context_->ClearRenderTargetView(renderTarget_.Get(), clear);
    return swapChain_->Present(1, 0);
}

} // namespace horizon_uwp
