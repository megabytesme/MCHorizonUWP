#include "Dx11Renderer.h"
#define GL_GLEXT_PROTOTYPES 1
#include <GLES3/gl3.h>
namespace horizon_uwp {
HRESULT Dx11Renderer::Initialize(IUnknown* window) noexcept {
    auto getDisplay = reinterpret_cast<PFNEGLGETPLATFORMDISPLAYEXTPROC>(eglGetProcAddress("eglGetPlatformDisplayEXT"));
    if (!getDisplay || !window) return E_FAIL;
    const EGLint displayAttributes[] = { EGL_PLATFORM_ANGLE_TYPE_ANGLE, EGL_PLATFORM_ANGLE_TYPE_D3D11_ANGLE, EGL_NONE };
    display_ = getDisplay(EGL_PLATFORM_ANGLE_ANGLE, EGL_DEFAULT_DISPLAY, displayAttributes);
    if (display_ == EGL_NO_DISPLAY || !eglInitialize(display_, nullptr, nullptr)) return E_FAIL;
    const EGLint configAttributes[] = { EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8, EGL_SURFACE_TYPE, EGL_WINDOW_BIT, EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT_KHR, EGL_NONE };
    EGLConfig config = nullptr; EGLint count = 0;
    if (!eglChooseConfig(display_, configAttributes, &config, 1, &count) || !count) return E_FAIL;
    const EGLint contextAttributes[] = { EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE };
    context_ = eglCreateContext(display_, config, EGL_NO_CONTEXT, contextAttributes);
    if (context_ == EGL_NO_CONTEXT) return E_FAIL;
    surface_ = eglCreateWindowSurface(display_, config, reinterpret_cast<IInspectable*>(window), nullptr);
    if (surface_ == EGL_NO_SURFACE) return E_FAIL;
    if (!eglMakeCurrent(display_, surface_, surface_, context_)) return E_FAIL;
    return S_OK;
}
HRESULT Dx11Renderer::Render() noexcept {
    glClearColor(0.35f, 0.08f, 0.18f, 1.0f);
    glClear(GL_COLOR_BUFFER_BIT);
    return eglSwapBuffers(display_, surface_) ? S_OK : E_FAIL;
}
}
