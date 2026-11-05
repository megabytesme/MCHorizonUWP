#include <windows.h>
#include <windows.gaming.input.h>
#include <windows.system.threading.h>

using namespace Windows::Gaming::Input;
using namespace Windows::System::Threading;

struct HorizonGamepadReading {
    UINT32 buttons;
    float left_x, left_y, right_x, right_y, left_trigger, right_trigger;
    UINT32 connected;
};
static_assert(sizeof(HorizonGamepadReading) == 32, "gamepad bridge ABI");

namespace {
Gamepad^ active = nullptr;
ThreadPoolTimer^ motor_timer = nullptr;

bool engaged(const GamepadReading& value) {
    return value.Buttons != GamepadButtons::None ||
        value.LeftThumbstickX > 0.5 || value.LeftThumbstickX < -0.5 ||
        value.LeftThumbstickY > 0.5 || value.LeftThumbstickY < -0.5 ||
        value.RightThumbstickX > 0.5 || value.RightThumbstickX < -0.5 ||
        value.RightThumbstickY > 0.5 || value.RightThumbstickY < -0.5 ||
        value.LeftTrigger > 0.5 || value.RightTrigger > 0.5;
}

void stop(Gamepad^ pad) {
    if (motor_timer != nullptr) { motor_timer->Cancel(); motor_timer = nullptr; }
    if (pad == nullptr) return;
    try { pad->Vibration = GamepadVibration{}; } catch (...) {}
}
}

extern "C" int __cdecl horizon_uwp_gamepad_read(HorizonGamepadReading* output) {
    if (!output) return -1;
    *output = HorizonGamepadReading{};
    try {
        auto pads = Gamepad::Gamepads;
        const auto count = pads->Size;
        bool present = false;
        for (unsigned int i = 0; i < count; ++i) if (pads->GetAt(i) == active) present = true;
        if (!present) { stop(active); active = count ? pads->GetAt(0) : nullptr; }
        if (active == nullptr) return 0;
        auto reading = active->GetCurrentReading();
        if (!engaged(reading)) {
            for (unsigned int i = 0; i < count; ++i) {
                auto candidate = pads->GetAt(i);
                if (candidate == active) continue;
                auto value = candidate->GetCurrentReading();
                if (engaged(value)) { stop(active); active = candidate; reading = value; break; }
            }
        }
        output->buttons = static_cast<UINT32>(reading.Buttons);
        output->left_x = static_cast<float>(reading.LeftThumbstickX);
        output->left_y = static_cast<float>(reading.LeftThumbstickY);
        output->right_x = static_cast<float>(reading.RightThumbstickX);
        output->right_y = static_cast<float>(reading.RightThumbstickY);
        output->left_trigger = static_cast<float>(reading.LeftTrigger);
        output->right_trigger = static_cast<float>(reading.RightTrigger);
        output->connected = count;
        return 1;
    } catch (...) {
        stop(active);
        active = nullptr;
        return -1;
    }
}

extern "C" int __cdecl horizon_uwp_gamepad_vibrate(float strong, float weak, UINT32 millis) {
    if (!(strong >= 0 && strong <= 1 && weak >= 0 && weak <= 1) || millis > 2000 || active == nullptr) return -1;
    try {
        stop(active);
        if (!millis) return 0;
        GamepadVibration value{};
        value.LeftMotor = strong;
        value.RightMotor = weak;
        active->Vibration = value;
        auto pad = active;
        Windows::Foundation::TimeSpan delay{};
        delay.Duration = static_cast<INT64>(millis) * 10000;
        motor_timer = ThreadPoolTimer::CreateTimer(ref new TimerElapsedHandler([pad](ThreadPoolTimer^) {
            try { pad->Vibration = GamepadVibration{}; } catch (...) {}
        }), delay);
        return 0;
    } catch (...) { stop(active); return -1; }
}
