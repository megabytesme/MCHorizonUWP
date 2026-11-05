#include "Dx11Renderer.h"
#include "PhoneProbeVS.h"
#include "PhoneProbePS.h"

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
        D3D_FEATURE_LEVEL_12_1, D3D_FEATURE_LEVEL_12_0,
        D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0,
        D3D_FEATURE_LEVEL_9_3, D3D_FEATURE_LEVEL_9_2,
        D3D_FEATURE_LEVEL_9_1,
    };
    ComPtr<ID3D11Device> maximumDevice;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, ARRAYSIZE(levels),
        D3D11_SDK_VERSION, &maximumDevice, &maximumFeatureLevel_, nullptr);
    if (hr == E_INVALIDARG) {
        // Older runtimes may reject the newer enum values outright.
        hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels + 2, ARRAYSIZE(levels) - 2,
            D3D11_SDK_VERSION, &maximumDevice, &maximumFeatureLevel_, nullptr);
    }
    if (FAILED(hr)) return hr;
    maximumDevice.Reset();
    // Test the lowest feature level independently of the maximum supported.
    const D3D_FEATURE_LEVEL baseline[] = { D3D_FEATURE_LEVEL_9_1 };
    hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, baseline, ARRAYSIZE(baseline),
        D3D11_SDK_VERSION, &device_, &featureLevel_, &context_);
    if (FAILED(hr)) return hr;

    if (FAILED(hr = device_->CreateVertexShader(g_phone_probe_vs, sizeof(g_phone_probe_vs), nullptr, &vertexShader_))) return hr;
    if (FAILED(hr = device_->CreatePixelShader(g_phone_probe_ps, sizeof(g_phone_probe_ps), nullptr, &pixelShader_))) return hr;
    const D3D11_INPUT_ELEMENT_DESC elements[] = {
        { "POSITION", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0 },
        { "TEXCOORD", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 8, D3D11_INPUT_PER_VERTEX_DATA, 0 },
    };
    if (FAILED(hr = device_->CreateInputLayout(elements, ARRAYSIZE(elements), g_phone_probe_vs, sizeof(g_phone_probe_vs), &inputLayout_))) return hr;
    const float quad[] = {
        -0.75f, -0.75f, 0.0f, 1.0f,  -0.75f, 0.75f, 0.0f, 0.0f,
         0.75f, -0.75f, 1.0f, 1.0f,   0.75f, 0.75f, 1.0f, 0.0f,
    };
    D3D11_BUFFER_DESC bufferDesc = {};
    bufferDesc.ByteWidth = sizeof(quad);
    bufferDesc.Usage = D3D11_USAGE_IMMUTABLE;
    bufferDesc.BindFlags = D3D11_BIND_VERTEX_BUFFER;
    D3D11_SUBRESOURCE_DATA bufferData = {};
    bufferData.pSysMem = quad;
    if (FAILED(hr = device_->CreateBuffer(&bufferDesc, &bufferData, &vertices_))) return hr;

    // RGBA tiles make texture sampling, orientation, and the shader visible.
    const unsigned char pixels[] = {
        255, 70, 70, 255,  70, 255, 70, 255,
        70, 70, 255, 255,  255, 230, 70, 255,
    };
    D3D11_TEXTURE2D_DESC textureDesc = {};
    textureDesc.Width = 2;
    textureDesc.Height = 2;
    textureDesc.MipLevels = 1;
    textureDesc.ArraySize = 1;
    textureDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    textureDesc.SampleDesc.Count = 1;
    textureDesc.Usage = D3D11_USAGE_IMMUTABLE;
    textureDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA textureData = {};
    textureData.pSysMem = pixels;
    textureData.SysMemPitch = 8;
    ComPtr<ID3D11Texture2D> texture;
    if (FAILED(hr = device_->CreateTexture2D(&textureDesc, &textureData, &texture))) return hr;
    if (FAILED(hr = device_->CreateShaderResourceView(texture.Get(), nullptr, &atlas_))) return hr;
    D3D11_SAMPLER_DESC samplerDesc = {};
    samplerDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
    samplerDesc.AddressU = samplerDesc.AddressV = samplerDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    samplerDesc.MaxLOD = D3D11_FLOAT32_MAX;
    if (FAILED(hr = device_->CreateSamplerState(&samplerDesc, &sampler_))) return hr;
    D3D11_RASTERIZER_DESC rasterizerDesc = {};
    rasterizerDesc.FillMode = D3D11_FILL_SOLID;
    rasterizerDesc.CullMode = D3D11_CULL_NONE;
    rasterizerDesc.DepthClipEnable = TRUE;
    if (FAILED(hr = device_->CreateRasterizerState(&rasterizerDesc, &rasterizer_))) return hr;

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
    ComPtr<ID3D11Resource> resource;
    renderTarget_->GetResource(&resource);
    ComPtr<ID3D11Texture2D> backBuffer;
    HRESULT hr = resource.As(&backBuffer);
    if (FAILED(hr)) return hr;
    D3D11_TEXTURE2D_DESC desc = {};
    backBuffer->GetDesc(&desc);
    D3D11_VIEWPORT viewport = { 0.0f, 0.0f, static_cast<float>(desc.Width), static_cast<float>(desc.Height), 0.0f, 1.0f };
    context_->RSSetViewports(1, &viewport);
    context_->RSSetState(rasterizer_.Get());
    ID3D11RenderTargetView* target = renderTarget_.Get();
    context_->OMSetRenderTargets(1, &target, nullptr);
    context_->IASetInputLayout(inputLayout_.Get());
    context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP);
    ID3D11Buffer* vertices = vertices_.Get();
    const UINT stride = 4 * sizeof(float), offset = 0;
    context_->IASetVertexBuffers(0, 1, &vertices, &stride, &offset);
    context_->VSSetShader(vertexShader_.Get(), nullptr, 0);
    context_->PSSetShader(pixelShader_.Get(), nullptr, 0);
    ID3D11ShaderResourceView* texture = atlas_.Get();
    ID3D11SamplerState* sampler = sampler_.Get();
    context_->PSSetShaderResources(0, 1, &texture);
    context_->PSSetSamplers(0, 1, &sampler);
    context_->Draw(4, 0);
    return swapChain_->Present(1, 0);
}

} // namespace horizon_uwp
