#pragma once

#include <stdint.h>
#include <windows.ui.core.h>
#include <windows.graphics.display.h>
#include <windows.devices.input.h>

// The payload is valid only for the duration of the callback. Callbacks run on
// the CoreWindow dispatcher thread. Dimensions and coordinates are physical
// pixels; wheel_delta uses Windows' 120-units-per-notch convention.
enum HorizonUwpEventKind : uint32_t {
    HORIZON_UWP_RESIZE = 1,
    HORIZON_UWP_FOCUS,
    HORIZON_UWP_VISIBILITY,
    HORIZON_UWP_CLOSE,
    HORIZON_UWP_KEY_DOWN,
    HORIZON_UWP_KEY_UP,
    HORIZON_UWP_CHARACTER,
    HORIZON_UWP_POINTER_MOVE,
    HORIZON_UWP_POINTER_DOWN,
    HORIZON_UWP_POINTER_UP,
    HORIZON_UWP_POINTER_WHEEL,
    HORIZON_UWP_MOUSE_DELTA,
    HORIZON_UWP_POINTER_CANCEL,
};

enum HorizonUwpEventFlags : uint32_t {
    HORIZON_UWP_EXTENDED_KEY = 1,
    HORIZON_UWP_KEY_REPEAT = 2,
    HORIZON_UWP_HORIZONTAL_WHEEL = 4,
};

struct HorizonUwpEvent {
    uint32_t kind;
    uint32_t flags;
    uint32_t value; // Boolean, VirtualKey, or UTF-16 code unit.
    uint32_t scan_code;
    uint32_t pointer_id;
    uint32_t pointer_type; // Windows.Devices.Input.PointerDeviceType.
    uint32_t buttons; // bit 0 left, 1 right, 2 middle, 3 X1, 4 X2.
    uint32_t button_change; // Windows.UI.Input.PointerUpdateKind.
    int32_t wheel_delta;
    float x;
    float y;
    float width;
    float height;
    float scale_factor;
};
static_assert(sizeof(HorizonUwpEvent) == 56, "Rust event ABI must match");

typedef void (__cdecl *HorizonUwpEventCallback)(void*, const HorizonUwpEvent*);
extern "C" uint32_t __cdecl horizon_uwp_window_size_stage();

namespace horizon_uwp {

// Own this alongside the app view; destroy it before releasing the window.
ref class CoreWindowEvents sealed {
internal:
    CoreWindowEvents(Windows::UI::Core::CoreWindow^ window,
                     HorizonUwpEventCallback callback, void* context);
    void EmitInitialSize();

public:
    virtual ~CoreWindowEvents();

private:
    void Emit(HorizonUwpEvent& event);
    void OnSize(Windows::UI::Core::CoreWindow^, Windows::UI::Core::WindowSizeChangedEventArgs^);
    void OnDpi(Windows::Graphics::Display::DisplayInformation^, Platform::Object^);
    void OnFocus(Windows::UI::Core::CoreWindow^, Windows::UI::Core::WindowActivatedEventArgs^);
    void OnVisibility(Windows::UI::Core::CoreWindow^, Windows::UI::Core::VisibilityChangedEventArgs^);
    void OnClose(Windows::UI::Core::CoreWindow^, Windows::UI::Core::CoreWindowEventArgs^);
    void OnKeyDown(Windows::UI::Core::CoreWindow^, Windows::UI::Core::KeyEventArgs^);
    void OnKeyUp(Windows::UI::Core::CoreWindow^, Windows::UI::Core::KeyEventArgs^);
    void OnCharacter(Windows::UI::Core::CoreWindow^, Windows::UI::Core::CharacterReceivedEventArgs^);
    void OnPointerMove(Windows::UI::Core::CoreWindow^, Windows::UI::Core::PointerEventArgs^);
    void OnPointerDown(Windows::UI::Core::CoreWindow^, Windows::UI::Core::PointerEventArgs^);
    void OnPointerUp(Windows::UI::Core::CoreWindow^, Windows::UI::Core::PointerEventArgs^);
    void OnPointerCancel(Windows::UI::Core::CoreWindow^, Windows::UI::Core::PointerEventArgs^);
    void OnPointerWheel(Windows::UI::Core::CoreWindow^, Windows::UI::Core::PointerEventArgs^);
    void OnMouseDelta(Windows::Devices::Input::MouseDevice^, Windows::Devices::Input::MouseEventArgs^);
    void EmitKey(uint32_t kind, Windows::UI::Core::KeyEventArgs^ args);
    void EmitPointer(uint32_t kind, Windows::UI::Core::PointerEventArgs^ args);

    Windows::UI::Core::CoreWindow^ window_;
    Windows::Graphics::Display::DisplayInformation^ display_;
    Windows::Devices::Input::MouseDevice^ mouse_;
    HorizonUwpEventCallback callback_;
    void* context_;
    float scale_factor_ = 1.0f;
    float width_ = 0.0f;
    float height_ = 0.0f;
    Windows::Foundation::EventRegistrationToken size_, dpi_, focus_, visibility_, close_;
    Windows::Foundation::EventRegistrationToken key_down_, key_up_, character_;
    Windows::Foundation::EventRegistrationToken pointer_move_, pointer_down_, pointer_up_, pointer_cancel_, pointer_wheel_;
    Windows::Foundation::EventRegistrationToken mouse_delta_;
};
}
