#include <windows.h>
#include <cstddef>
#include <cstdlib>
#include <cstring>
#include <string>

using namespace Windows::ApplicationModel::Activation;
using namespace Windows::ApplicationModel::Core;
using namespace Windows::Foundation;
using namespace Windows::Storage;
using namespace Windows::System::Threading;
using namespace Windows::UI::Core;

extern "C" int wgpu_world_scale_probe(void (*report)(const char*, size_t));

namespace {
HANDLE report_file = INVALID_HANDLE_VALUE;

extern "C" void append_report(const char* text, size_t length) {
    if (report_file == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    WriteFile(report_file, text, static_cast<DWORD>(length), &written, nullptr);
    FlushFileBuffers(report_file);
}

[[noreturn]] void unsupported_surface_callback(const char* symbol) {
    constexpr char prefix[] = "Unexpected non-offscreen backend callback ";
    append_report(prefix, sizeof(prefix) - 1);
    append_report(symbol, std::strlen(symbol));
    append_report("\n", 1);
    std::abort();
}

void run_probe() {
    const std::wstring path = std::wstring(ApplicationData::Current->LocalFolder->Path->Data()) +
                              L"\\wgpu-world-scale-probe.txt";
    report_file = CreateFile2(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
    if (report_file == INVALID_HANDLE_VALUE) return;
    constexpr char start[] = "UWP world scale probe startup\n";
    append_report(start, sizeof(start) - 1);
    wgpu_world_scale_probe(append_report);
    CloseHandle(report_file);
    report_file = INVALID_HANDLE_VALUE;
}
}

// The exact product rlib retains methods for backends and surfaces this headless runner never selects.
extern "C" void* horizon_uwp_acquire_game_swapchain_panel() { unsupported_surface_callback(__func__); }
extern "C" void horizon_uwp_release_game_swapchain_panel(void*) { unsupported_surface_callback(__func__); }
extern "C" int horizon_uwp_detach_game_swapchain() { unsupported_surface_callback(__func__); }
extern "C" int horizon_uwp_attach_game_swapchain(void*) { unsupported_surface_callback(__func__); }
extern "C" void horizon_uwp_get_surface_metrics(UINT*, UINT*, double*, double*) { unsupported_surface_callback(__func__); }
extern "C" int horizon_uwp_is_ui_thread() { unsupported_surface_callback(__func__); }
extern "C" int horizon_uwp_dispatch_ui_sync(void (*)(void*), void*) { unsupported_surface_callback(__func__); }
extern "C" int horizon_uwp_angle_window_properties(void*, UINT, UINT, void**) { unsupported_surface_callback(__func__); }
extern "C" void horizon_uwp_release_angle_window_properties(void*) { unsupported_surface_callback(__func__); }

ref class WorldScaleProbeView sealed : IFrameworkView {
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
            this, &WorldScaleProbeView::activated);
    }
    virtual void SetWindow(CoreWindow^ window) { window_ = window; }
    virtual void Load(Platform::String^) {}
    virtual void Run() {
        window_->Dispatcher->ProcessEvents(CoreProcessEventsOption::ProcessUntilQuit);
    }
    virtual void Uninitialize() {}
};

ref class WorldScaleProbeSource sealed : IFrameworkViewSource {
public:
    virtual IFrameworkView^ CreateView() { return ref new WorldScaleProbeView(); }
};

[Platform::MTAThread]
int main(Platform::Array<Platform::String^>^) {
    CoreApplication::Run(ref new WorldScaleProbeSource());
    return 0;
}
