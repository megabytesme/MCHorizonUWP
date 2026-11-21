#include <windows.h>
#include <cstdio>
#include <cstring>
#include <string>

using namespace Windows::ApplicationModel::Activation;
using namespace Windows::ApplicationModel::Core;
using namespace Windows::Devices::Sensors;
using namespace Windows::Foundation;
using namespace Windows::Storage;
using namespace Windows::System::Threading;
using namespace Windows::UI::Core;

namespace {
void log_line(HANDLE file, const char* text) {
    DWORD written = 0;
    WriteFile(file, text, static_cast<DWORD>(std::strlen(text)), &written, nullptr);
    FlushFileBuffers(file);
}

void measure(HANDLE file) {
    auto gyro = Gyrometer::GetDefault();
    if (gyro == nullptr) {
        log_line(file, "Gyrometer.GetDefault=null\n");
        return;
    }
    log_line(file, "Gyrometer.GetDefault=available\n");
    char line[256];
    const auto minimum = gyro->MinimumReportInterval;
    const auto interval = minimum > 16u ? minimum : 16u;
    sprintf_s(line, "MinimumReportIntervalMs=%u\nReportIntervalMs=%u\n", minimum, interval);
    log_line(file, line);
    gyro->ReportInterval = interval;

    // The asynchronous delegate may retain the signal until this short-lived process exits.
    const HANDLE sample_ready = CreateEventExW(nullptr, nullptr, CREATE_EVENT_MANUAL_RESET, EVENT_MODIFY_STATE | SYNCHRONIZE);
    if (sample_ready == nullptr) {
        log_line(file, "ReadingChanged=signal-creation-failed\n");
    } else {
        const auto observed = gyro->ReadingChanged += ref new TypedEventHandler<Gyrometer^, GyrometerReadingChangedEventArgs^>(
            [sample_ready](Gyrometer^, GyrometerReadingChangedEventArgs^ args) {
                if (args->Reading != nullptr) SetEvent(sample_ready);
            });
        const DWORD wait = WaitForSingleObjectEx(sample_ready, 2000, FALSE);
        gyro->ReadingChanged -= observed;
        sprintf_s(line, "ReadingChanged=%s\n", wait == WAIT_OBJECT_0 ? "sample" : (wait == WAIT_TIMEOUT ? "timeout" : "wait-failed"));
        log_line(file, line);
    }

    auto reading = gyro->GetCurrentReading();
    if (reading == nullptr) {
        log_line(file, "GetCurrentReading=null\n");
    } else {
        sprintf_s(line, "GetCurrentReading=sample\nTimestamp100ns=%lld\nAngularVelocityDegreesPerSecond=%.6f,%.6f,%.6f\n",
                  static_cast<long long>(reading->Timestamp.UniversalTime), reading->AngularVelocityX,
                  reading->AngularVelocityY, reading->AngularVelocityZ);
        log_line(file, line);
    }
    gyro->ReportInterval = 0;
}

void run_probe() {
    const auto folder = ApplicationData::Current->LocalFolder->Path;
    const auto path = std::wstring(folder->Data()) + L"\\gyro-probe.txt";
    const HANDLE file = CreateFile2(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    log_line(file, "Gyrometer capability probe 1.0.0.0\n");
    try {
        measure(file);
    } catch (Platform::Exception^ error) {
        char line[64];
        sprintf_s(line, "WinRTHResult=0x%08x\n", static_cast<unsigned>(error->HResult));
        log_line(file, line);
    }
    log_line(file, "probe complete\n");
    CloseHandle(file);
}
}

ref class GyrometerProbeView sealed : IFrameworkView {
    CoreWindow^ window_;
    bool started_ = false;

    void activated(CoreApplicationView^, IActivatedEventArgs^) {
        window_->Activate();
        if (started_) return;
        started_ = true;
        auto dispatcher = window_->Dispatcher;
        ThreadPool::RunAsync(ref new WorkItemHandler([dispatcher](IAsyncAction^) {
            run_probe();
            dispatcher->RunAsync(CoreDispatcherPriority::Normal, ref new DispatchedHandler([] {
                CoreApplication::Exit();
            }));
        }));
    }

public:
    virtual void Initialize(CoreApplicationView^ view) {
        view->Activated += ref new TypedEventHandler<CoreApplicationView^, IActivatedEventArgs^>(
            this, &GyrometerProbeView::activated);
    }
    virtual void SetWindow(CoreWindow^ window) { window_ = window; }
    virtual void Load(Platform::String^) {}
    virtual void Run() { window_->Dispatcher->ProcessEvents(CoreProcessEventsOption::ProcessUntilQuit); }
    virtual void Uninitialize() {}
};

ref class GyrometerProbeSource sealed : IFrameworkViewSource {
public:
    virtual IFrameworkView^ CreateView() { return ref new GyrometerProbeView(); }
};

[Platform::MTAThread]
int main(Platform::Array<Platform::String^>^) {
    CoreApplication::Run(ref new GyrometerProbeSource());
    return 0;
}
