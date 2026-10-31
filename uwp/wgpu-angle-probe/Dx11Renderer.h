#pragma once
#include <windows.h>
namespace horizon_uwp {
class Dx11Renderer final {
public:
    ~Dx11Renderer();
    HRESULT Initialize(IUnknown* window) noexcept;
    HRESULT Resize() noexcept { return S_OK; }
    HRESULT Render() noexcept;
private:
    void* probe_ = nullptr;
};
}
