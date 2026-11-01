#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <windows.applicationmodel.h>
#include <windows.storage.h>

struct Report {
    int32_t status;
    uint32_t width, height;
    uint64_t non_black;
    uint8_t first[4][4], patched[4][4];
};
extern "C" int32_t entity_probe_run(void*, Report*);

// These satisfy unreferenced winit code pulled in by the Horizon type crate.
// /OPT:REF must remove them; the probe does not use the winit window backend.
extern "C" void horizon_uwp_get_metrics() {}
extern "C" void horizon_uwp_set_cursor_visible() {}
extern "C" void horizon_uwp_set_cursor_position() {}
extern "C" void horizon_uwp_window_handle() {}
extern "C" void horizon_uwp_set_ime_allowed() {}

using namespace Windows::ApplicationModel::Activation;
using namespace Windows::ApplicationModel::Core;
using namespace Windows::Foundation;
using namespace Windows::UI::Core;

ref class ProbeView sealed : IFrameworkView {
public:
    virtual void Initialize(CoreApplicationView^ view) {
        view->Activated += ref new TypedEventHandler<CoreApplicationView^, IActivatedEventArgs^>(this, &ProbeView::Activated);
    }
    virtual void SetWindow(CoreWindow^ window) {
        window_ = window;
        Report report = {};
        const int result = entity_probe_run(reinterpret_cast<void*>(window), &report);
        auto base = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        auto path = Platform::String::Concat(base, ref new Platform::String(L"\\entity-probe.txt"));
        HANDLE file = CreateFile2(path->Data(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
        if (file != INVALID_HANDLE_VALUE) {
            char text[512] = {};
            int length = sprintf_s(text, "result=%d size=%ux%u non_black=%llu first=%u,%u,%u patched=%u,%u,%u\n",
                result, report.width, report.height, static_cast<unsigned long long>(report.non_black),
                report.first[0][0], report.first[0][1], report.first[0][2],
                report.patched[0][0], report.patched[0][1], report.patched[0][2]);
            DWORD written = 0;
            if (length > 0) WriteFile(file, text, static_cast<DWORD>(length), &written, nullptr);
            CloseHandle(file);
        }
    }
    virtual void Load(Platform::String^) {}
    virtual void Run() {
        window_->Dispatcher->ProcessEvents(CoreProcessEventsOption::ProcessAllIfPresent);
        CoreApplication::Exit();
    }
    virtual void Uninitialize() {}
private:
    void Activated(CoreApplicationView^, IActivatedEventArgs^) { window_->Activate(); }
    CoreWindow^ window_;
};
ref class ProbeSource sealed : IFrameworkViewSource {
public: virtual IFrameworkView^ CreateView() { return ref new ProbeView(); }
};
[Platform::MTAThread]
int main(Platform::Array<Platform::String^>^) { CoreApplication::Run(ref new ProbeSource()); return 0; }
