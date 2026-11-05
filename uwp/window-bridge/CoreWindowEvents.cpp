#include "CoreWindowEvents.h"
#include <windows.ui.input.h>
#include <atomic>

using namespace Windows::Foundation;
using namespace Windows::Graphics::Display;
using namespace Windows::UI::Core;
using namespace Windows::UI::Input;
using namespace Windows::Devices::Input;

namespace horizon_uwp {

static std::atomic<uint32_t> current_size_stage{0};

} // namespace horizon_uwp

extern "C" uint32_t __cdecl horizon_uwp_window_size_stage() {
    return horizon_uwp::current_size_stage.load(std::memory_order_acquire);
}

namespace horizon_uwp {

CoreWindowEvents::CoreWindowEvents(CoreWindow^ w, HorizonUwpEventCallback callback, void* context)
    : window_(w), callback_(callback), context_(context) {
    if (!w || !callback) throw ref new Platform::InvalidArgumentException();
    display_ = DisplayInformation::GetForCurrentView();
    dpi_ = display_->DpiChanged += ref new TypedEventHandler<DisplayInformation^, Platform::Object^>(this, &CoreWindowEvents::OnDpi);
    size_ = w->SizeChanged += ref new TypedEventHandler<CoreWindow^, WindowSizeChangedEventArgs^>(this, &CoreWindowEvents::OnSize);
    focus_ = w->Activated += ref new TypedEventHandler<CoreWindow^, WindowActivatedEventArgs^>(this, &CoreWindowEvents::OnFocus);
    visibility_ = w->VisibilityChanged += ref new TypedEventHandler<CoreWindow^, VisibilityChangedEventArgs^>(this, &CoreWindowEvents::OnVisibility);
    close_ = w->Closed += ref new TypedEventHandler<CoreWindow^, CoreWindowEventArgs^>(this, &CoreWindowEvents::OnClose);
    key_down_ = w->KeyDown += ref new TypedEventHandler<CoreWindow^, KeyEventArgs^>(this, &CoreWindowEvents::OnKeyDown);
    key_up_ = w->KeyUp += ref new TypedEventHandler<CoreWindow^, KeyEventArgs^>(this, &CoreWindowEvents::OnKeyUp);
    character_ = w->CharacterReceived += ref new TypedEventHandler<CoreWindow^, CharacterReceivedEventArgs^>(this, &CoreWindowEvents::OnCharacter);
    pointer_move_ = w->PointerMoved += ref new TypedEventHandler<CoreWindow^, PointerEventArgs^>(this, &CoreWindowEvents::OnPointerMove);
    pointer_down_ = w->PointerPressed += ref new TypedEventHandler<CoreWindow^, PointerEventArgs^>(this, &CoreWindowEvents::OnPointerDown);
    pointer_up_ = w->PointerReleased += ref new TypedEventHandler<CoreWindow^, PointerEventArgs^>(this, &CoreWindowEvents::OnPointerUp);
    pointer_cancel_ = w->PointerCaptureLost += ref new TypedEventHandler<CoreWindow^, PointerEventArgs^>(this, &CoreWindowEvents::OnPointerCancel);
    pointer_wheel_ = w->PointerWheelChanged += ref new TypedEventHandler<CoreWindow^, PointerEventArgs^>(this, &CoreWindowEvents::OnPointerWheel);
    try {
        mouse_ = MouseDevice::GetForCurrentView();
        if (mouse_) mouse_delta_ = mouse_->MouseMoved += ref new TypedEventHandler<MouseDevice^, MouseEventArgs^>(this, &CoreWindowEvents::OnMouseDelta);
    } catch (Platform::Exception^) { mouse_ = nullptr; }
}

CoreWindowEvents::~CoreWindowEvents() {
    if (mouse_) mouse_->MouseMoved -= mouse_delta_;
    display_->DpiChanged -= dpi_;
    window_->SizeChanged -= size_;
    window_->Activated -= focus_;
    window_->VisibilityChanged -= visibility_;
    window_->Closed -= close_;
    window_->KeyDown -= key_down_;
    window_->KeyUp -= key_up_;
    window_->CharacterReceived -= character_;
    window_->PointerMoved -= pointer_move_;
    window_->PointerPressed -= pointer_down_;
    window_->PointerReleased -= pointer_up_;
    window_->PointerCaptureLost -= pointer_cancel_;
    window_->PointerWheelChanged -= pointer_wheel_;
}

void CoreWindowEvents::Emit(HorizonUwpEvent& event) {
    event.scale_factor = scale_factor_;
    callback_(context_, &event);
}

void CoreWindowEvents::EmitInitialSize() {
    current_size_stage.store(1, std::memory_order_release);
    try {
        const auto scale = display_->LogicalDpi / 96.0f;
        scale_factor_ = scale;
    } catch (Platform::Exception^ error) {
        if (width_ <= 0.0f || height_ <= 0.0f)
            throw ref new Platform::Exception(error->HResult, L"Reading DisplayInformation.LogicalDpi failed.");
    }
    current_size_stage.store(2, std::memory_order_release);
    try {
        const auto bounds = window_->Bounds;
        width_ = bounds.Width * scale_factor_;
        height_ = bounds.Height * scale_factor_;
    } catch (Platform::Exception^ error) {
        // XAML can temporarily make view metrics unavailable while its root is
        // detached. Continue with the last size observed before the handoff.
        if (width_ <= 0.0f || height_ <= 0.0f)
            throw ref new Platform::Exception(error->HResult, L"Reading CoreWindow.Bounds failed.");
    }
    HorizonUwpEvent event{};
    event.kind = HORIZON_UWP_RESIZE;
    event.width = width_;
    event.height = height_;
    current_size_stage.store(3, std::memory_order_release);
    Emit(event);
    // Keep the last completed step until the next resize begins. If WinRT
    // raises a delayed exception while unwinding the caller, clearing this to
    // zero would erase the only useful handoff diagnostic.
    current_size_stage.store(4, std::memory_order_release);
}

void CoreWindowEvents::OnSize(CoreWindow^, WindowSizeChangedEventArgs^) { EmitInitialSize(); }
void CoreWindowEvents::OnDpi(DisplayInformation^, Platform::Object^) { EmitInitialSize(); }
void CoreWindowEvents::OnFocus(CoreWindow^, WindowActivatedEventArgs^ args) {
    HorizonUwpEvent e{}; e.kind = HORIZON_UWP_FOCUS;
    e.value = args->WindowActivationState != CoreWindowActivationState::Deactivated; Emit(e);
}
void CoreWindowEvents::OnVisibility(CoreWindow^, VisibilityChangedEventArgs^ args) {
    HorizonUwpEvent e{}; e.kind = HORIZON_UWP_VISIBILITY; e.value = args->Visible; Emit(e);
}
void CoreWindowEvents::OnClose(CoreWindow^, CoreWindowEventArgs^) {
    HorizonUwpEvent e{}; e.kind = HORIZON_UWP_CLOSE; Emit(e);
}
void CoreWindowEvents::EmitKey(uint32_t kind, KeyEventArgs^ args) {
    HorizonUwpEvent e{}; e.kind = kind; e.value = static_cast<uint32_t>(args->VirtualKey);
    auto status = args->KeyStatus; e.scan_code = status.ScanCode;
    if (status.IsExtendedKey) e.flags |= HORIZON_UWP_EXTENDED_KEY;
    if (status.WasKeyDown) e.flags |= HORIZON_UWP_KEY_REPEAT;
    Emit(e);
}
void CoreWindowEvents::OnKeyDown(CoreWindow^, KeyEventArgs^ e) { EmitKey(HORIZON_UWP_KEY_DOWN, e); }
void CoreWindowEvents::OnKeyUp(CoreWindow^, KeyEventArgs^ e) { EmitKey(HORIZON_UWP_KEY_UP, e); }
void CoreWindowEvents::OnCharacter(CoreWindow^, CharacterReceivedEventArgs^ args) {
    HorizonUwpEvent e{}; e.kind = HORIZON_UWP_CHARACTER; e.value = args->KeyCode; Emit(e);
}
void CoreWindowEvents::EmitPointer(uint32_t kind, PointerEventArgs^ args) {
    HorizonUwpEvent e{}; e.kind = kind;
    auto point = args->CurrentPoint; auto properties = point->Properties;
    e.x = point->Position.X * scale_factor_; e.y = point->Position.Y * scale_factor_;
    e.pointer_id = point->PointerId;
    e.pointer_type = static_cast<uint32_t>(point->PointerDevice->PointerDeviceType);
    e.buttons = (properties->IsLeftButtonPressed ? 1u : 0u) |
                (properties->IsRightButtonPressed ? 2u : 0u) |
                (properties->IsMiddleButtonPressed ? 4u : 0u) |
                (properties->IsXButton1Pressed ? 8u : 0u) |
                (properties->IsXButton2Pressed ? 16u : 0u);
    e.button_change = static_cast<uint32_t>(properties->PointerUpdateKind);
    e.wheel_delta = properties->MouseWheelDelta;
    if (properties->IsHorizontalMouseWheel) e.flags |= HORIZON_UWP_HORIZONTAL_WHEEL;
    Emit(e);
}
void CoreWindowEvents::OnPointerMove(CoreWindow^, PointerEventArgs^ e) { EmitPointer(HORIZON_UWP_POINTER_MOVE, e); }
void CoreWindowEvents::OnPointerDown(CoreWindow^, PointerEventArgs^ e) { EmitPointer(HORIZON_UWP_POINTER_DOWN, e); }
void CoreWindowEvents::OnPointerUp(CoreWindow^, PointerEventArgs^ e) { EmitPointer(HORIZON_UWP_POINTER_UP, e); }
void CoreWindowEvents::OnPointerCancel(CoreWindow^, PointerEventArgs^ e) { EmitPointer(HORIZON_UWP_POINTER_CANCEL, e); }
void CoreWindowEvents::OnPointerWheel(CoreWindow^, PointerEventArgs^ e) { EmitPointer(HORIZON_UWP_POINTER_WHEEL, e); }
void CoreWindowEvents::OnMouseDelta(MouseDevice^, MouseEventArgs^ args) {
    HorizonUwpEvent e{}; e.kind = HORIZON_UWP_MOUSE_DELTA;
    e.x = static_cast<float>(args->MouseDelta.X); e.y = static_cast<float>(args->MouseDelta.Y);
    Emit(e);
}

}
