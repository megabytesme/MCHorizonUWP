#if defined(_MSC_VER) && _MSC_VER >= 1940 && defined(WINAPI_FAMILY)
extern "C" __declspec(dllimport) char* __cdecl getenv(const char*);
extern "C" __declspec(dllimport) int __cdecl system(const char*);
#endif
#include <windows.h>
#include <windows.devices.sensors.h>
#include <windows.graphics.display.h>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <mutex>

using namespace Windows::Devices::Sensors;
using namespace Windows::Foundation;
using namespace Windows::Graphics::Display;

namespace {
using Clock = std::chrono::steady_clock;
constexpr auto max_sample_age = std::chrono::milliseconds(240);
std::mutex lifecycle;
std::mutex mutex;
int display_orientation = 0;
Gyrometer^ sensor;
Windows::Foundation::EventRegistrationToken reading_token{};
bool requested = false;
bool visible = true;
bool running = false;
bool aligned = false;
int aligned_orientation = 0;
std::uint32_t generation = 0;
std::int64_t previous_timestamp = 0;
float turned_x = 0.0f;
float turned_y = 0.0f;
Clock::time_point batch_begin{};

void clear_samples() {
    previous_timestamp = 0;
    turned_x = 0.0f;
    turned_y = 0.0f;
    batch_begin = {};
}

bool align(Gyrometer^ device) {
    try {
        auto orientation = static_cast<DisplayOrientations>(display_orientation);
        if (orientation != DisplayOrientations::None) device->ReadingTransform = orientation;
        return true;
    } catch (...) { return false; }
}

void reading_changed(std::uint32_t expected_generation, GyrometerReadingChangedEventArgs^ args) {
    try {
        auto reading = args->Reading;
        if (reading == nullptr) return;
        const auto timestamp = reading->Timestamp.UniversalTime;
        std::lock_guard<std::mutex> guard(mutex);
        if (!running || !aligned || generation != expected_generation) return;
        if (previous_timestamp != 0 && timestamp > previous_timestamp) {
            const double seconds = static_cast<double>(timestamp - previous_timestamp) / 10000000.0;
            if (seconds > 0.0 && seconds <= 0.1) {
                constexpr double radians_per_degree = 0.017453292519943295;
                const double x = reading->AngularVelocityX * seconds * radians_per_degree;
                const double y = reading->AngularVelocityY * seconds * radians_per_degree;
                if (std::isfinite(x) && std::isfinite(y)) {
                    const auto now = Clock::now();
                    if (batch_begin == Clock::time_point{} || now - batch_begin > max_sample_age) {
                        batch_begin = now;
                        turned_x = 0.0f;
                        turned_y = 0.0f;
                    }
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
        running = false;
        old_sensor = sensor;
        old_token = reading_token;
        sensor = nullptr;
        reading_token = Windows::Foundation::EventRegistrationToken{};
        clear_samples();
    }
    if (old_sensor != nullptr) {
        try { old_sensor->ReadingChanged -= old_token; } catch (...) {}
        try { old_sensor->ReportInterval = 0; } catch (...) {}
    }
}

void start_sensor() {
    Gyrometer^ candidate = nullptr;
    Windows::Foundation::EventRegistrationToken token{};
    try {
        candidate = Gyrometer::GetDefault();
        if (candidate == nullptr || !align(candidate)) return;
        candidate->ReportInterval = (std::max)(16u, candidate->MinimumReportInterval);
        std::uint32_t started_generation;
        {
            std::lock_guard<std::mutex> guard(mutex);
            started_generation = ++generation;
        }
        token = candidate->ReadingChanged += ref new TypedEventHandler<Gyrometer^, GyrometerReadingChangedEventArgs^>(
            [started_generation](Gyrometer^, GyrometerReadingChangedEventArgs^ args) {
                reading_changed(started_generation, args);
            });
    } catch (...) {
        if (candidate != nullptr) {
            try { candidate->ReportInterval = 0; } catch (...) {}
        }
        return;
    }

    std::lock_guard<std::mutex> guard(mutex);
    sensor = candidate;
    reading_token = token;
    running = true;
    aligned = true;
    aligned_orientation = display_orientation;
    clear_samples();
}

void reconcile() {
    const bool wanted = requested && visible;
    if (running && (!wanted || !aligned)) stop_sensor();
    if (wanted && !running) start_sensor();
}
}

extern "C" void __cdecl horizon_uwp_gyro_set_orientation(int value) {
    try {
        std::lock_guard<std::mutex> transition(lifecycle);
        if (value == display_orientation) return;
        display_orientation = value;
        if (!running) return;
        {
            std::lock_guard<std::mutex> guard(mutex);
            aligned = false;
        }
        const bool ready = align(sensor);
        std::lock_guard<std::mutex> guard(mutex);
        clear_samples();
        aligned = ready;
        aligned_orientation = display_orientation;
    } catch (...) {}
}

extern "C" int __cdecl horizon_uwp_gyro_available() {
    try { return Gyrometer::GetDefault() != nullptr ? 1 : 0; }
    catch (...) { return 0; }
}

extern "C" int __cdecl horizon_uwp_gyro_enable(int enabled) {
    try {
        std::lock_guard<std::mutex> transition(lifecycle);
        requested = enabled != 0;
        reconcile();
        return running && aligned ? 1 : 0;
    } catch (...) { return 0; }
}

extern "C" void __cdecl horizon_uwp_gyro_set_visible(int is_visible) {
    try {
        std::lock_guard<std::mutex> transition(lifecycle);
        visible = is_visible != 0;
        reconcile();
    } catch (...) {}
}

extern "C" int __cdecl horizon_uwp_gyro_take(float* x, float* y, int* rotation) {
    if (x == nullptr || y == nullptr || rotation == nullptr) return 0;
    try {
        std::lock_guard<std::mutex> guard(mutex);
        if (!running || !aligned || batch_begin == Clock::time_point{}) return 0;
        if (Clock::now() - batch_begin > max_sample_age) {
            clear_samples();
            return 0;
        }
        *x = turned_x;
        *y = turned_y;
        // Legacy callers use the low two bits for an extra quarter turn. New
        // callers can reset their filters when the display orientation changes.
        *rotation = aligned_orientation << 2;
        batch_begin = {};
        turned_x = 0.0f;
        turned_y = 0.0f;
        return 1;
    } catch (...) { return 0; }
}
