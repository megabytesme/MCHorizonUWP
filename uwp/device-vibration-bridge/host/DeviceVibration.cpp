#include <windows.h>
#include <roapi.h>
#include <windows.foundation.h>
#include <windows.foundation.metadata.h>
#include <windows.devices.haptics.h>

// Public Windows.Phone.PhoneContract ABI. Using its two small interfaces keeps
// the optional phone API usable with the base SDK when the Mobile extension
// headers are not installed; activation simply fails on non-phone devices.
MIDL_INTERFACE("1B4A6595-CFCD-4E08-92FB-C1906D04498C")
IHorizonPhoneVibration : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Vibrate(ABI::Windows::Foundation::TimeSpan duration) = 0;
    virtual HRESULT STDMETHODCALLTYPE Cancel() = 0;
};
MIDL_INTERFACE("332FD2F1-1C69-4C91-949E-4BB67A85BDC7")
IHorizonPhoneVibrationStatics : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE GetDefault(IHorizonPhoneVibration** result) = 0;
};

using namespace Windows::Foundation;
using namespace Windows::Devices::Haptics;
namespace {
volatile LONG generation = 0;

bool phone_pulse(UINT32 millis, bool cancel) {
    static const wchar_t name[] = L"Windows.Phone.Devices.Notification.VibrationDevice";
    HSTRING_HEADER header{};
    HSTRING class_name = nullptr;
    if (FAILED(WindowsCreateStringReference(name, ARRAYSIZE(name) - 1, &header, &class_name))) return false;
    IHorizonPhoneVibrationStatics* factory = nullptr;
    if (FAILED(RoGetActivationFactory(class_name, __uuidof(IHorizonPhoneVibrationStatics),
                                      reinterpret_cast<void**>(&factory)))) return false;
    IHorizonPhoneVibration* device = nullptr;
    HRESULT result = factory->GetDefault(&device);
    factory->Release();
    if (FAILED(result) || !device) return false;
    ABI::Windows::Foundation::TimeSpan duration{};
    duration.Duration = static_cast<INT64>(millis) * 10000;
    result = cancel ? device->Cancel() : device->Vibrate(duration);
    device->Release();
    return SUCCEEDED(result);
}

bool still_current(LONG value) {
    return InterlockedCompareExchange(&generation, 0, 0) == value;
}

void universal_pulse(UINT32 millis, float intensity, LONG token) {
    if (!Windows::Foundation::Metadata::ApiInformation::IsTypePresent(
            L"Windows.Devices.Haptics.VibrationDevice")) return;
    auto access = VibrationDevice::RequestAccessAsync();
    access->Completed = ref new AsyncOperationCompletedHandler<VibrationAccessStatus>(
        [millis, intensity, token](IAsyncOperation<VibrationAccessStatus>^ request, Windows::Foundation::AsyncStatus status) {
            try {
                if (status != Windows::Foundation::AsyncStatus::Completed || !still_current(token) ||
                    request->GetResults() != VibrationAccessStatus::Allowed) return;
                auto lookup = VibrationDevice::GetDefaultAsync();
                lookup->Completed = ref new AsyncOperationCompletedHandler<VibrationDevice^>(
                    [millis, intensity, token](IAsyncOperation<VibrationDevice^>^ operation, Windows::Foundation::AsyncStatus result) {
                        try {
                            if (result != Windows::Foundation::AsyncStatus::Completed || !still_current(token)) return;
                            auto device = operation->GetResults();
                            if (device == nullptr) return;
                            auto controller = device->SimpleHapticsController;
                            if (controller == nullptr) return;
                            auto feedback = controller->SupportedFeedback;
                            SimpleHapticsControllerFeedback^ discrete = nullptr;
                            for (unsigned int i = 0; i < feedback->Size; ++i) {
                                auto item = feedback->GetAt(i);
                                const auto waveform = item->Waveform;
                                if (controller->IsPlayDurationSupported &&
                                    (waveform == KnownSimpleHapticsControllerWaveforms::RumbleContinuous ||
                                     waveform == KnownSimpleHapticsControllerWaveforms::BuzzContinuous)) {
                                    TimeSpan duration{};
                                    duration.Duration = static_cast<INT64>(millis) * 10000;
                                    controller->SendHapticFeedbackForDuration(item,
                                        controller->IsIntensitySupported ? intensity : 1.0, duration);
                                    return;
                                }
                                if (discrete == nullptr && item->Duration.Duration > 0 &&
                                    item->Duration.Duration <= 250LL * 10000 &&
                                    (waveform == KnownSimpleHapticsControllerWaveforms::Press ||
                                     waveform == KnownSimpleHapticsControllerWaveforms::Click)) discrete = item;
                            }
                            if (discrete != nullptr) {
                                if (controller->IsIntensitySupported) controller->SendHapticFeedback(discrete, intensity);
                                else controller->SendHapticFeedback(discrete);
                            }
                        } catch (...) {}
                    });
            } catch (...) {}
        });
}
}

extern "C" int __cdecl horizon_uwp_device_vibrate(UINT32 millis, float intensity) {
    if (!millis || millis > 250 || !(intensity >= 0 && intensity <= 1)) return -1;
    const LONG token = InterlockedIncrement(&generation);
    try {
        if (phone_pulse(millis, false)) return 0;
        universal_pulse(millis, intensity, token);
        return 0;
    } catch (...) { return -1; }
}

extern "C" void __cdecl horizon_uwp_device_vibration_cancel() {
    InterlockedIncrement(&generation);
    phone_pulse(0, true);
}
