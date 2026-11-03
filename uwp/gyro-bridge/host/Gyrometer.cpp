#include <windows.h>
#include <windows.devices.sensors.h>
#include <windows.graphics.display.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <mutex>

using namespace Windows::Devices::Sensors;
using namespace Windows::Foundation;
using namespace Windows::Graphics::Display;

namespace {
std::mutex mutex;
Gyrometer^ sensor;
Windows::Foundation::EventRegistrationToken reading_token{};
bool requested = false;
bool visible = true;
bool starting = false;
bool running = false;
std::uint32_t generation = 0;
std::int64_t previous_timestamp = 0;
float turned_x = 0.0f;
float turned_y = 0.0f;

void reading_changed(std::uint32_t expected_generation, GyrometerReadingChangedEventArgs^ args) {
    try {
        auto reading = args->Reading;
        if (reading == nullptr) return;
        const auto timestamp = reading->Timestamp.UniversalTime;
        std::lock_guard<std::mutex> guard(mutex);
        if (!running || generation != expected_generation) return;
        if (previous_timestamp != 0 && timestamp > previous_timestamp) {
            const double seconds = static_cast<double>(timestamp - previous_timestamp) / 10000000.0;
            if (seconds > 0.0 && seconds <= 0.1) {
                constexpr double radians_per_degree = 0.017453292519943295;
                const double x = reading->AngularVelocityX * seconds * radians_per_degree;
                const double y = reading->AngularVelocityY * seconds * radians_per_degree;
                if (std::isfinite(x) && std::isfinite(y)) {
                    turned_x = std::clamp(turned_x + static_cast<float>(x), -6.2831853f, 6.2831853f);
                    turned_y = std::clamp(turned_y + static_cast<float>(y), -6.2831853f, 6.2831853f);
                }
            }
        }
        previous_timestamp = timestamp;
    } catch (...) {}
}

void stop_sensor() {
    Gyrometer^ old_sensor = nullptr;
    Windows::Foundation::EventRegistrationToken old_token{};
    {
        std::lock_guard<std::mutex> guard(mutex);
        ++generation;
        starting = false;
        running = false;
        old_sensor = sensor;
        old_token = reading_token;
        sensor = nullptr;
        reading_token = Windows::Foundation::EventRegistrationToken{};
        previous_timestamp = 0;
        turned_x = 0.0f;
        turned_y = 0.0f;
    }
    if (old_sensor != nullptr) {
        try { old_sensor->ReadingChanged -= old_token; } catch (...) {}
        try { old_sensor->ReportInterval = 0; } catch (...) {}
    }
}

bool start_sensor() {
    std::uint32_t started_generation = 0;
    {
        std::lock_guard<std::mutex> guard(mutex);
        if (!requested || !visible || running) return running;
        if (starting) return false;
        starting = true;
        started_generation = ++generation;
    }

    Gyrometer^ candidate = nullptr;
    Windows::Foundation::EventRegistrationToken token{};
    try {
        candidate = Gyrometer::GetDefault();
        if (candidate == nullptr) {
            std::lock_guard<std::mutex> guard(mutex);
            if (generation == started_generation) starting = false;
            return false;
        }
        const auto display = DisplayInformation::GetForCurrentView();
        if (display != nullptr) candidate->ReadingTransform = display->CurrentOrientation;
        candidate->ReportInterval = (std::max)(16u, candidate->MinimumReportInterval);
        token = candidate->ReadingChanged += ref new TypedEventHandler<Gyrometer^, GyrometerReadingChangedEventArgs^>(
            [started_generation](Gyrometer^, GyrometerReadingChangedEventArgs^ args) {
                reading_changed(started_generation, args);
            });
    } catch (...) {
        std::lock_guard<std::mutex> guard(mutex);
        if (generation == started_generation) starting = false;
        return false;
    }

    bool keep = false;
    {
        std::lock_guard<std::mutex> guard(mutex);
        keep = generation == started_generation && requested && visible;
        starting = false;
        if (keep) {
            sensor = candidate;
            reading_token = token;
            running = true;
            previous_timestamp = 0;
            turned_x = 0.0f;
            turned_y = 0.0f;
        }
    }
    if (!keep) {
        try { candidate->ReadingChanged -= token; } catch (...) {}
        try { candidate->ReportInterval = 0; } catch (...) {}
    }
    return keep;
}

void reconcile() {
    bool should_run = false;
    bool is_running = false;
    {
        std::lock_guard<std::mutex> guard(mutex);
        should_run = requested && visible;
        is_running = running || starting;
    }
    if (should_run && !is_running) start_sensor();
    else if (!should_run && is_running) stop_sensor();
}
}

extern "C" int __cdecl horizon_uwp_gyro_available() {
    try { return Gyrometer::GetDefault() != nullptr ? 1 : 0; }
    catch (...) { return 0; }
}

extern "C" int __cdecl horizon_uwp_gyro_enable(int enabled) {
    try {
        {
            std::lock_guard<std::mutex> guard(mutex);
            requested = enabled != 0;
        }
        reconcile();
        std::lock_guard<std::mutex> guard(mutex);
        return running ? 1 : 0;
    } catch (...) { return 0; }
}

extern "C" void __cdecl horizon_uwp_gyro_set_visible(int is_visible) {
    try {
        {
            std::lock_guard<std::mutex> guard(mutex);
            visible = is_visible != 0;
        }
        reconcile();
    } catch (...) {}
}

extern "C" int __cdecl horizon_uwp_gyro_take(float* x, float* y, int* rotation) {
    if (x == nullptr || y == nullptr || rotation == nullptr) return 0;
    try {
        Gyrometer^ current = nullptr;
        {
            std::lock_guard<std::mutex> guard(mutex);
            if (!running) return 0;
            current = sensor;
        }
        auto display = DisplayInformation::GetForCurrentView();
        if (current != nullptr && display != nullptr) current->ReadingTransform = display->CurrentOrientation;
        std::lock_guard<std::mutex> guard(mutex);
        if (!running) return 0;
        *x = turned_x;
        *y = turned_y;
        *rotation = 0;
        turned_x = 0.0f;
        turned_y = 0.0f;
        return 1;
    } catch (...) { return 0; }
}
