#include <windows.h>
#include <windows.storage.h>
#include <cstdio>
#include <atomic>
#include "Dx11Renderer.h"
#ifdef HORIZON_UWP_GAME_BRIDGE
#include "../window-bridge/CoreWindowEvents.h"
#include <windows.applicationmodel.h>
#include <windows.foundation.metadata.h>
#include <windows.graphics.display.h>
#include <windows.ui.viewmanagement.h>

static Windows::UI::Core::CoreWindow^ current_window;
static std::atomic<DWORD> current_window_thread{0};
static HorizonUwpEvent pending_events[4096];
static uint32_t event_read, event_write;

static void __cdecl queue_event(void*, const HorizonUwpEvent* event) {
    const uint32_t next = (event_write + 1) % 4096;
    if (next == event_read) event_read = (event_read + 1) % 4096;
    pending_events[event_write] = *event;
    event_write = next;
}
extern "C" void* __cdecl horizon_uwp_window_handle() {
    // CoreWindow must only be handed out on its owning UI thread.
    const DWORD owner = current_window_thread.load(std::memory_order_acquire);
    if (!owner || GetCurrentThreadId() != owner) return nullptr;
    return reinterpret_cast<void*>(current_window);
}
extern "C" void __cdecl horizon_uwp_pump_events() {
    current_window->Dispatcher->ProcessEvents(Windows::UI::Core::CoreProcessEventsOption::ProcessAllIfPresent);
}
extern "C" int __cdecl horizon_uwp_pop_event(HorizonUwpEvent* event) {
    if (!event || event_read == event_write) return 0;
    *event = pending_events[event_read];
    event_read = (event_read + 1) % 4096;
    return 1;
}
extern "C" void __cdecl horizon_uwp_get_metrics(uint32_t* width, uint32_t* height, double* scale) {
    const double s = Windows::Graphics::Display::DisplayInformation::GetForCurrentView()->LogicalDpi / 96.0;
    *width = static_cast<uint32_t>(current_window->Bounds.Width * s);
    *height = static_cast<uint32_t>(current_window->Bounds.Height * s);
    *scale = s;
}
extern "C" void __cdecl horizon_uwp_set_cursor_visible(int visible) {
    if (!current_window || !current_window->Dispatcher->HasThreadAccess) return;
    current_window->PointerCursor = visible
        ? ref new Windows::UI::Core::CoreCursor(Windows::UI::Core::CoreCursorType::Arrow, 0)
        : nullptr;
}
extern "C" void __cdecl horizon_uwp_set_ime_allowed(int allowed) {
    if (!current_window || !current_window->Dispatcher->HasThreadAccess) return;
    try {
        auto pane = Windows::UI::ViewManagement::InputPane::GetForCurrentView();
        if (pane) {
            if (allowed) pane->TryShow(); else pane->TryHide();
        }
    } catch (Platform::Exception^) {
        // InputPane is optional on devices without a touch keyboard.
    }
}

extern "C" int __cdecl horizon_uwp_set_cursor_position(double x, double y) {
    if (!current_window || !current_window->Dispatcher->HasThreadAccess) return 0;
    const double scale = Windows::Graphics::Display::DisplayInformation::GetForCurrentView()->LogicalDpi / 96.0;
    try {
        current_window->PointerPosition = Windows::Foundation::Point(static_cast<float>(x / scale), static_cast<float>(y / scale));
        return 1;
    } catch (Platform::Exception^) { return 0; }
}
extern "C" int32_t __cdecl horizon_uwp_run(const uint16_t*, size_t, const uint16_t*, size_t);
extern "C" void __cdecl horizon_uwp_report_error(const uint8_t* text, size_t len) {
    if (!text && len != 0) return;
    auto base = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
    auto file = Platform::String::Concat(base, ref new Platform::String(L"\\horizon-startup-error.txt"));
    HANDLE h = CreateFile2(file->Data(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    while (len != 0) {
        const DWORD chunk = len > MAXDWORD ? MAXDWORD : static_cast<DWORD>(len);
        DWORD written = 0;
        if (!WriteFile(h, text, chunk, &written, nullptr) || written == 0) break;
        text += written;
        len -= written;
    }
    CloseHandle(h);
}
#ifdef HORIZON_UWP_FRAME_PROBE
extern "C" void __cdecl horizon_uwp_frame_presented() {
    static bool recorded = false;
    if (recorded) return;
    auto base = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
    auto file = Platform::String::Concat(base, ref new Platform::String(L"\\frame-presented.txt"));
    HANDLE h = CreateFile2(file->Data(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
    if (h != INVALID_HANDLE_VALUE) {
        const char marker[] = "wgpu frame presented\n";
        DWORD count = 0;
        if (WriteFile(h, marker, sizeof(marker) - 1, &count, nullptr) && count == sizeof(marker) - 1) recorded = true;
        CloseHandle(h);
    }
}
#endif
#endif

#ifdef HORIZON_UWP_CORE_BRIDGE
extern "C" int horizon_uwp_protocol_version();
#endif

using namespace Windows::ApplicationModel::Activation;
using namespace Windows::ApplicationModel::Core;
using namespace Windows::Foundation;
using namespace Windows::UI::Core;

ref class AppView sealed : IFrameworkView {
internal:
    AppView() : renderer_(new horizon_uwp::Dx11Renderer()), closed_(false), visible_(true) {}
public:
    virtual ~AppView() {
#ifdef HORIZON_UWP_GAME_BRIDGE
        delete window_events_;
        current_window_thread.store(0, std::memory_order_release);
        current_window = nullptr;
#endif
        delete renderer_;
    }
public:
    virtual void Initialize(CoreApplicationView^ view) {
#ifdef HORIZON_UWP_CORE_BRIDGE
        if (horizon_uwp_protocol_version() <= 0) throw ref new Platform::FailureException();
#endif
        view->Activated += ref new TypedEventHandler<CoreApplicationView^, IActivatedEventArgs^>(this, &AppView::Activated);
    }
    virtual void SetWindow(CoreWindow^ window) {
        window_ = window;
#ifdef HORIZON_UWP_GAME_BRIDGE
        current_window = window;
        current_window_thread.store(GetCurrentThreadId(), std::memory_order_release);
        window_events_ = ref new horizon_uwp::CoreWindowEvents(window, queue_event, nullptr);
        window_events_->EmitInitialSize();
#endif
        window->Closed += ref new TypedEventHandler<CoreWindow^, CoreWindowEventArgs^>(this, &AppView::Closed);
        window->VisibilityChanged += ref new TypedEventHandler<CoreWindow^, VisibilityChangedEventArgs^>(this, &AppView::VisibilityChanged);
        window->SizeChanged += ref new TypedEventHandler<CoreWindow^, WindowSizeChangedEventArgs^>(this, &AppView::SizeChanged);
#ifndef HORIZON_UWP_GAME_BRIDGE
        Check(renderer_->Initialize(reinterpret_cast<IUnknown*>(window)));
        auto local_path = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        auto diagnostic_path = Platform::String::Concat(local_path, ref new Platform::String(L"\\d3d11-feature-levels.txt"));
        HANDLE diagnostic = CreateFile2(diagnostic_path->Data(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
        if (diagnostic != INVALID_HANDLE_VALUE) {
            char message[128];
            const int length = sprintf_s(message, "maximum=0x%04x\nrendering=0x%04x\n", static_cast<unsigned>(renderer_->MaximumFeatureLevel()), static_cast<unsigned>(renderer_->FeatureLevel()));
            DWORD written = 0;
            if (length > 0) WriteFile(diagnostic, message, static_cast<DWORD>(length), &written, nullptr);
            CloseHandle(diagnostic);
        }
#endif
    }
    virtual void Load(Platform::String^) {}
    virtual void Run() {
#ifdef HORIZON_UWP_GAME_BRIDGE
        auto local_path = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        auto resources = Platform::String::Concat(local_path, ref new Platform::String(L"\\resources"));
        // Prefer prepared, writable local resources; a sideloaded phone package
        // can include the same prepared tree under its installed location.
        auto manifest = Platform::String::Concat(resources, ref new Platform::String(L"\\horizon-resources.json"));
        HANDLE local_manifest = CreateFile2(manifest->Data(), GENERIC_READ,
                                            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                            OPEN_EXISTING, nullptr);
        if (local_manifest == INVALID_HANDLE_VALUE) {
            auto installed = Windows::ApplicationModel::Package::Current->InstalledLocation->Path;
            resources = Platform::String::Concat(installed, ref new Platform::String(L"\\resources"));
            // A client may instead depend on a versioned resource framework.
            // Only use an unambiguous framework from this package's graph.
            Platform::String^ framework_resources = nullptr;
            auto dependencies = Windows::ApplicationModel::Package::Current->Dependencies;
            for (unsigned int index = 0; index < dependencies->Size; ++index) {
                auto dependency = dependencies->GetAt(index);
                auto name = dependency->Id->Name;
                constexpr wchar_t prefix[] = L"Horizon.Asset.";
                if (wcsncmp(name->Data(), prefix, _countof(prefix) - 1) != 0) continue;
                if (wcscmp(dependency->Id->Publisher->Data(), L"CN=HorizonUWPPrototype") != 0) continue;
                if (framework_resources != nullptr) throw ref new Platform::FailureException();
                framework_resources = Platform::String::Concat(
                    dependency->InstalledLocation->Path, ref new Platform::String(L"\\resources"));
            }
            if (framework_resources != nullptr) resources = framework_resources;
        } else {
            CloseHandle(local_manifest);
        }
        auto game = Platform::String::Concat(local_path, ref new Platform::String(L"\\game"));
        const int32_t result = horizon_uwp_run(reinterpret_cast<const uint16_t*>(resources->Data()), resources->Length(),
                                               reinterpret_cast<const uint16_t*>(game->Data()), game->Length());
        if (result != 0) throw ref new Platform::FailureException();
#else
        while (!closed_) {
            if (visible_) {
                window_->Dispatcher->ProcessEvents(CoreProcessEventsOption::ProcessAllIfPresent);
                if (!closed_) Check(renderer_->Render());
            } else {
                window_->Dispatcher->ProcessEvents(CoreProcessEventsOption::ProcessOneAndAllPending);
            }
        }
#endif
    }
    virtual void Uninitialize() {}
private:
    void Check(HRESULT hr) { if (FAILED(hr)) throw Platform::Exception::CreateException(hr); }
    void Activated(CoreApplicationView^, IActivatedEventArgs^) {
        window_->Activate();
#if defined(HORIZON_UWP_GAME_BRIDGE) && defined(_M_ARM)
        // Mobile already occupies a full window, but full-screen mode also
        // asks the shell to hide its status/navigation bars. The shell may
        // decline; in that case the normal CoreWindow remains usable.
        if (attempted_mobile_fullscreen_) return;
        attempted_mobile_fullscreen_ = true;
        try {
            if (Windows::Foundation::Metadata::ApiInformation::IsApiContractPresent(
                    L"Windows.Phone.PhoneContract", 1)) {
                Windows::UI::ViewManagement::ApplicationView::GetForCurrentView()
                    ->TryEnterFullScreenMode();
            }
        } catch (Platform::Exception^) {
            // Continue windowed when the phone shell doesn't support it.
        }
#endif
    }
    void Closed(CoreWindow^, CoreWindowEventArgs^) { closed_ = true; }
    void VisibilityChanged(CoreWindow^, VisibilityChangedEventArgs^ args) { visible_ = args->Visible; }
    void SizeChanged(CoreWindow^, WindowSizeChangedEventArgs^) {
#ifndef HORIZON_UWP_GAME_BRIDGE
        Check(renderer_->Resize());
#endif
    }
    CoreWindow^ window_;
    horizon_uwp::Dx11Renderer* renderer_;
#if defined(HORIZON_UWP_GAME_BRIDGE) && defined(_M_ARM)
    bool attempted_mobile_fullscreen_ = false;
#endif
#ifdef HORIZON_UWP_GAME_BRIDGE
    horizon_uwp::CoreWindowEvents^ window_events_;
#endif
    bool closed_;
    bool visible_;
};

ref class AppViewSource sealed : IFrameworkViewSource {
public:
    virtual IFrameworkView^ CreateView() { return ref new AppView(); }
};

[Platform::MTAThread]
int main(Platform::Array<Platform::String^>^) {
    CoreApplication::Run(ref new AppViewSource());
    return 0;
}

