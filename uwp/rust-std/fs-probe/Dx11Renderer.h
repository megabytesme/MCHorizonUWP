#pragma once
#include <windows.h>
namespace horizon_uwp {
class Dx11Renderer final {
public:
    HRESULT Initialize(IUnknown*) noexcept;
    HRESULT Resize() noexcept { return S_OK; }
    HRESULT Render() noexcept { SleepEx(16, FALSE); return S_OK; }
};
}
