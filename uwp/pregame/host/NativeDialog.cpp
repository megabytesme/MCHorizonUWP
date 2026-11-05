// Native CoreWindow startup page. It releases its swap chain before the game
// creates its own, so both the phone renderer and the page use the same view.
#if defined(_MSC_VER) && _MSC_VER >= 1940 && defined(WINAPI_FAMILY)
extern "C" __declspec(dllimport) char* __cdecl getenv(const char*);
extern "C" __declspec(dllimport) int __cdecl system(const char*);
#endif
#include <windows.h>
#include <windows.ui.core.h>
#include <windows.graphics.display.h>
#include <windows.gaming.input.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <string>
#include "../../window-bridge/CoreWindowEvents.h"

using Microsoft::WRL::ComPtr;
using namespace Windows::UI::Core;
using namespace Windows::Gaming::Input;
extern "C" void* __cdecl horizon_uwp_window_handle();

namespace {
struct Button { const wchar_t* label; int choice; D2D1_RECT_F bounds; };
ComPtr<ID3D11Device> device;
ComPtr<IDXGISwapChain1> swapchain;
ComPtr<ID2D1Factory> factory;
ComPtr<ID2D1RenderTarget> target;
ComPtr<IDWriteFactory> fonts;
ComPtr<IDWriteTextFormat> title_font, body_font, button_font;
ComPtr<ID2D1SolidColorBrush> brush;
std::wstring message;
Button buttons[2] = {};
int button_count = 0, selected = 0, choice = -1;
unsigned int last_pad_buttons = 0;
bool visible = false;

HRESULT make_target() {
    ComPtr<IDXGISurface> surface;
    HRESULT hr = swapchain->GetBuffer(0, IID_PPV_ARGS(&surface));
    if (FAILED(hr)) return hr;
    const float dpi = Windows::Graphics::Display::DisplayInformation::GetForCurrentView()->LogicalDpi;
    const auto props = D2D1::RenderTargetProperties(
        D2D1_RENDER_TARGET_TYPE_DEFAULT,
        D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_IGNORE),
        dpi, dpi);
    if (FAILED(hr = factory->CreateDxgiSurfaceRenderTarget(surface.Get(), &props, &target))) return hr;
    return target->CreateSolidColorBrush(D2D1::ColorF(D2D1::ColorF::White), &brush);
}

HRESULT initialize() {
    if (swapchain) return S_OK;
    auto window = reinterpret_cast<IUnknown*>(horizon_uwp_window_handle());
    if (!window) return E_UNEXPECTED;
    D3D_FEATURE_LEVEL level;
    const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_9_1 };
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 1, D3D11_SDK_VERSION,
        &device, &level, nullptr);
    if (FAILED(hr)) return hr;
    ComPtr<IDXGIDevice> dxgi;
    ComPtr<IDXGIAdapter> adapter;
    ComPtr<IDXGIFactory2> dxgi_factory;
    if (FAILED(hr = device.As(&dxgi)) ||
        FAILED(hr = dxgi->GetAdapter(&adapter)) ||
        FAILED(hr = adapter->GetParent(IID_PPV_ARGS(&dxgi_factory)))) return hr;
    DXGI_SWAP_CHAIN_DESC1 desc = {};
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.Scaling = DXGI_SCALING_NONE;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    if (FAILED(hr = dxgi_factory->CreateSwapChainForCoreWindow(device.Get(), window,
        &desc, nullptr, &swapchain))) return hr;
    if (FAILED(hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,
        __uuidof(ID2D1Factory), nullptr, reinterpret_cast<void**>(factory.GetAddressOf())))) return hr;
    if (FAILED(hr = DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(fonts.GetAddressOf())))) return hr;
    if (FAILED(hr = fonts->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_SEMI_BOLD,
        DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 28, L"en-us", &title_font)) ||
        FAILED(hr = fonts->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_NORMAL,
        DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 16, L"en-us", &body_font)) ||
        FAILED(hr = fonts->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_SEMI_BOLD,
        DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 16, L"en-us", &button_font))) return hr;
    button_font->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
    button_font->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
    return make_target();
}

HRESULT draw() {
    if (!visible || !target || !swapchain) return E_UNEXPECTED;
    const auto size = target->GetSize();
    const float margin = 20.0f;
    const float width = (std::max)(1.0f, size.width - 2 * margin);
    target->BeginDraw();
    target->Clear(D2D1::ColorF(0.055f, 0.085f, 0.13f));
    brush->SetColor(D2D1::ColorF(0.88f, 0.94f, 1.0f));
    target->DrawText(L"Horizon", 7, title_font.Get(),
        D2D1::RectF(margin, 24, margin + width, 66), brush.Get());
    brush->SetColor(D2D1::ColorF(0.74f, 0.82f, 0.90f));
    target->DrawText(message.c_str(), static_cast<UINT32>(message.size()), body_font.Get(),
        D2D1::RectF(margin, 76, margin + width, (std::max)(80.0f, size.height - 150)),
        brush.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
    const float button_height = 44.0f;
    const float top = (std::max)(size.height - 24.0f - button_count * 52.0f, 100.0f);
    for (int i = 0; i < button_count; ++i) {
        buttons[i].bounds = D2D1::RectF(margin, top + i * 52.0f, margin + width,
                                       top + i * 52.0f + button_height);
        brush->SetColor(i == selected ? D2D1::ColorF(0.20f, 0.48f, 0.70f)
                                     : D2D1::ColorF(0.13f, 0.23f, 0.34f));
        target->FillRectangle(buttons[i].bounds, brush.Get());
        brush->SetColor(D2D1::ColorF(0.96f, 0.98f, 1.0f));
        target->DrawText(buttons[i].label, static_cast<UINT32>(wcslen(buttons[i].label)),
                         button_font.Get(), buttons[i].bounds, brush.Get());
    }
    HRESULT hr = target->EndDraw();
    return FAILED(hr) ? hr : swapchain->Present(1, 0);
}

void add(const wchar_t* label, int value) {
    buttons[button_count++] = { label, value, {} };
}
void choose(int index) {
    if (index >= 0 && index < button_count) choice = buttons[index].choice;
}
void move(int delta) {
    if (!button_count) return;
    selected = (selected + delta + button_count) % button_count;
    if (FAILED(draw())) choice = -2;
}
}

extern "C" int __cdecl horizon_uwp_dialog_show(const unsigned char* text, size_t length, unsigned int kind) {
    if (!text || !length || length > 4096 || (visible && choice == 0)) return -1;
    try {
        const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<const char*>(text), static_cast<int>(length), nullptr, 0);
        if (count <= 0) return -1;
        message.resize(count);
        if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<const char*>(text), static_cast<int>(length), &message[0], count) != count) return -1;
        button_count = 0;
        switch (kind) {
        case 0: add(L"Start game", 1); add(L"Microsoft account", 2); break;
        case 1: add(L"Sign in", 1); add(L"Saved account", 2); break;
        case 2: add(L"Cancel", 1); break;
        case 3: add(L"OK", 1); break;
        case 4: add(L"Remove account", 1); add(L"Cancel", 2); break;
        case 5: add(L"Use saved account", 1); add(L"Sign out", 2); break;
        case 6: add(L"Continue", 1); add(L"Cancel", 2); break;
        default: return -1;
        }
        selected = (kind == 4 || kind == 6) ? 1 : 0;
        choice = 0;
        if (FAILED(initialize())) { choice = -1; return -1; }
        visible = true;
        if (FAILED(draw())) { choice = -1; return -1; }
        return 0;
    } catch (...) { choice = -1; return -1; }
}
extern "C" int __cdecl horizon_uwp_dialog_poll() { return choice; }
extern "C" void __cdecl horizon_uwp_dialog_cancel() { choice = -1; }
extern "C" void __cdecl horizon_uwp_page_profile(const unsigned char*, size_t) {}

extern "C" void __cdecl horizon_uwp_page_event(const HorizonUwpEvent* event) {
    if (!event || !visible || choice != 0) return;
    try {
        if (event->kind == HORIZON_UWP_RESIZE) {
            brush.Reset(); target.Reset();
            if (FAILED(swapchain->ResizeBuffers(2, 0, 0, DXGI_FORMAT_B8G8R8A8_UNORM, 0)) ||
                FAILED(make_target()) || FAILED(draw())) choice = -2;
        } else if (event->kind == HORIZON_UWP_POINTER_UP) {
            const float scale = event->scale_factor > 0 ? event->scale_factor : 1;
            const float x = event->x / scale, y = event->y / scale;
            for (int i = 0; i < button_count; ++i) {
                const auto& r = buttons[i].bounds;
                if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) { choose(i); break; }
            }
        } else if (event->kind == HORIZON_UWP_KEY_DOWN) {
            if (event->value == VK_UP || event->value == VK_LEFT) move(-1);
            else if (event->value == VK_DOWN || event->value == VK_RIGHT || event->value == VK_TAB) move(1);
            else if (event->value == VK_RETURN || event->value == VK_SPACE) choose(selected);
            else if (event->value == VK_ESCAPE) choice = -1;
        }
    } catch (...) { choice = -2; }
}
extern "C" void __cdecl horizon_uwp_page_tick() {
    if (!visible || choice != 0) return;
    try {
        auto pads = Gamepad::Gamepads;
        unsigned int now = 0;
        if (pads->Size) now = static_cast<unsigned int>(pads->GetAt(0)->GetCurrentReading().Buttons);
        const unsigned int pressed = now & ~last_pad_buttons;
        last_pad_buttons = now;
        if (pressed & static_cast<unsigned int>(GamepadButtons::DPadUp)) move(-1);
        if (pressed & static_cast<unsigned int>(GamepadButtons::DPadDown)) move(1);
        if (pressed & static_cast<unsigned int>(GamepadButtons::A)) choose(selected);
        if (pressed & static_cast<unsigned int>(GamepadButtons::B)) choice = -1;
    } catch (...) {}
}
extern "C" void __cdecl horizon_uwp_page_shutdown() {
    visible = false;
    brush.Reset(); target.Reset(); button_font.Reset(); body_font.Reset(); title_font.Reset();
    fonts.Reset(); factory.Reset();
    if (device) {
        ComPtr<ID3D11DeviceContext> context;
        device->GetImmediateContext(&context);
        if (context) { context->ClearState(); context->Flush(); }
    }
    swapchain.Reset(); device.Reset();
}
