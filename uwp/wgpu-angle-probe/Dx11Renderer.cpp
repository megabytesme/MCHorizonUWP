#include "Dx11Renderer.h"
extern "C" void* wgpu_probe_create(void* window);
extern "C" int wgpu_probe_render(void* probe);
extern "C" void wgpu_probe_destroy(void* probe);
namespace horizon_uwp {
Dx11Renderer::~Dx11Renderer() { if (probe_) wgpu_probe_destroy(probe_); }
HRESULT Dx11Renderer::Initialize(IUnknown* window) noexcept {
    probe_ = wgpu_probe_create(window);
    return probe_ ? S_OK : E_FAIL;
}
HRESULT Dx11Renderer::Render() noexcept { return wgpu_probe_render(probe_) == 0 ? S_OK : E_FAIL; }
}
