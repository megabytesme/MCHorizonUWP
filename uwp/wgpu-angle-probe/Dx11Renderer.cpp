#include "Dx11Renderer.h"
extern "C" void* wgpu_probe_create(void* window);
extern "C" int wgpu_probe_render(void* probe);
extern "C" int wgpu_probe_horizon_shader(void* probe);
extern "C" int wgpu_probe_terrain_shader(void* probe);
extern "C" size_t wgpu_probe_error(char* buffer, size_t length);
extern "C" void wgpu_probe_destroy(void* probe);
namespace horizon_uwp {
Dx11Renderer::~Dx11Renderer() { if (probe_) wgpu_probe_destroy(probe_); }
HRESULT Dx11Renderer::Initialize(IUnknown* window) noexcept {
    probe_ = wgpu_probe_create(window);
    if (!probe_ || wgpu_probe_horizon_shader(probe_) != 0 || wgpu_probe_terrain_shader(probe_) != 0) {
        char error[4096] = {};
        wgpu_probe_error(error, sizeof(error));
        OutputDebugStringA(error);
        return E_FAIL;
    }
    return S_OK;
}
HRESULT Dx11Renderer::Render() noexcept { return wgpu_probe_render(probe_) == 0 ? S_OK : E_FAIL; }
}
