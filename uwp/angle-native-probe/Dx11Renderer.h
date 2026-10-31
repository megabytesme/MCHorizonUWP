#pragma once
#include <windows.h>
#include <EGL/egl.h>
#include <EGL/eglext.h>
namespace horizon_uwp {
class Dx11Renderer final {
public:
    HRESULT Initialize(IUnknown* window) noexcept;
    HRESULT Resize() noexcept { return S_OK; }
    HRESULT Render() noexcept;
private:
    EGLDisplay display_ = EGL_NO_DISPLAY;
    EGLSurface surface_ = EGL_NO_SURFACE;
    EGLContext context_ = EGL_NO_CONTEXT;
};
}
