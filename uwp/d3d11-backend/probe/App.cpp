// UWP lifecycle glue for the Rust-owned Direct3D 9_1 renderer test.
#include <windows.h>
#include <windows.storage.h>
#ifdef HORIZON_D3D11_PHONE_DIALOG
#include <windows.ui.popups.h>
#include <cstring>
#endif

extern "C" void* __cdecl horizon_d3d11_probe_create(void* window);
extern "C" bool __cdecl horizon_d3d11_probe_render(void* probe);
extern "C" bool __cdecl horizon_d3d11_probe_resize(void* probe);
extern "C" void __cdecl horizon_d3d11_probe_destroy(void* probe);
extern "C" void __cdecl horizon_d3d11_probe_report(const unsigned char* text, size_t size) {
    auto base = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
    auto path = Platform::String::Concat(base, ref new Platform::String(L"\\rust-d3d11-probe.txt"));
    HANDLE file = CreateFile2(path->Data(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    if (size <= MAXDWORD) WriteFile(file, text, static_cast<DWORD>(size), &written, nullptr);
    CloseHandle(file);
#ifdef HORIZON_D3D11_PHONE_DIALOG
    try {
        const bool passed = size >= 4 && std::memcmp(text, "PASS", 4) == 0;
        auto content = passed
            ? L"PASS: Direct3D 9_1 textures, depth, transparency, overlays and terrain conversion worked."
            : L"FAIL: the Direct3D 9_1 test did not produce the expected pixels. Details are saved in app storage.";
        auto dialog = ref new Windows::UI::Popups::MessageDialog(ref new Platform::String(content), ref new Platform::String(L"Horizon D3D11 test"));
        dialog->ShowAsync();
    } catch (Platform::Exception^) {
        // The diagnostic file remains available if the dialog cannot open.
    }
#endif
}

using namespace Windows::ApplicationModel::Activation;
using namespace Windows::ApplicationModel::Core;
using namespace Windows::Foundation;
using namespace Windows::UI::Core;

ref class AppView sealed : IFrameworkView {
public:
    AppView() : probe_(nullptr), closed_(false), visible_(true) {}
    virtual ~AppView() { if (probe_) horizon_d3d11_probe_destroy(probe_); }
    virtual void Initialize(CoreApplicationView^ view) {
        view->Activated += ref new TypedEventHandler<CoreApplicationView^, IActivatedEventArgs^>(this, &AppView::Activated);
    }
    virtual void SetWindow(CoreWindow^ window) {
        window_ = window;
        window->Closed += ref new TypedEventHandler<CoreWindow^, CoreWindowEventArgs^>(this, &AppView::Closed);
        window->VisibilityChanged += ref new TypedEventHandler<CoreWindow^, VisibilityChangedEventArgs^>(this, &AppView::VisibilityChanged);
        window->SizeChanged += ref new TypedEventHandler<CoreWindow^, WindowSizeChangedEventArgs^>(this, &AppView::SizeChanged);
        probe_ = horizon_d3d11_probe_create(reinterpret_cast<void*>(window));
        if (!probe_) throw ref new Platform::FailureException();
    }
    virtual void Load(Platform::String^) {}
    virtual void Run() {
        while (!closed_) {
            window_->Dispatcher->ProcessEvents(visible_ ? CoreProcessEventsOption::ProcessAllIfPresent : CoreProcessEventsOption::ProcessOneAndAllPending);
            if (visible_ && !closed_ && !horizon_d3d11_probe_render(probe_)) throw ref new Platform::FailureException();
        }
    }
    virtual void Uninitialize() {}
private:
    void Activated(CoreApplicationView^, IActivatedEventArgs^) { window_->Activate(); }
    void Closed(CoreWindow^, CoreWindowEventArgs^) { closed_ = true; }
    void VisibilityChanged(CoreWindow^, VisibilityChangedEventArgs^ args) { visible_ = args->Visible; }
    void SizeChanged(CoreWindow^, WindowSizeChangedEventArgs^) {
        if (probe_ && !horizon_d3d11_probe_resize(probe_)) throw ref new Platform::FailureException();
    }
    CoreWindow^ window_;
    void* probe_;
    bool closed_, visible_;
};
ref class AppViewSource sealed : IFrameworkViewSource {
public:
    virtual IFrameworkView^ CreateView() { return ref new AppView(); }
};
[Platform::MTAThread]
int main(Platform::Array<Platform::String^>^) { CoreApplication::Run(ref new AppViewSource()); return 0; }
