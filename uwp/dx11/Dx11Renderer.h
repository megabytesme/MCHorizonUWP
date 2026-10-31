#pragma once

#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <wrl/client.h>

namespace horizon_uwp {

// Minimal D3D11 presentation probe. This is not the Horizon renderer.
class Dx11Renderer final {
public:
    HRESULT Initialize(IUnknown* coreWindow) noexcept;
    HRESULT Resize() noexcept;
    HRESULT Render() noexcept;

    D3D_FEATURE_LEVEL FeatureLevel() const noexcept { return featureLevel_; }
    ID3D11Device* Device() const noexcept { return device_.Get(); }

private:
    HRESULT CreateRenderTarget() noexcept;
    Microsoft::WRL::ComPtr<IUnknown> window_;
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
    Microsoft::WRL::ComPtr<IDXGISwapChain1> swapChain_;
    Microsoft::WRL::ComPtr<ID3D11RenderTargetView> renderTarget_;
    D3D_FEATURE_LEVEL featureLevel_ = D3D_FEATURE_LEVEL_9_1;
};

} // namespace horizon_uwp
