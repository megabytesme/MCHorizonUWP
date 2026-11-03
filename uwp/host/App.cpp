#include <windows.h>
#include <windows.storage.h>
#include <cstdio>
#include <cstring>
#include <atomic>
#include "Dx11Renderer.h"
#ifdef HORIZON_UWP_GAME_BRIDGE
#include "../window-bridge/CoreWindowEvents.h"
#include <windows.applicationmodel.h>
#include <windows.foundation.metadata.h>
#include <windows.graphics.display.h>
#include <windows.ui.viewmanagement.h>
#include <windows.ui.popups.h>

static Windows::UI::Core::CoreWindow^ current_window;
static std::atomic<DWORD> current_window_thread{0};
#ifdef HORIZON_UWP_ACCOUNT
static std::atomic<bool> account_window_closed{false};
static std::atomic<bool> account_window_visible{false};
static bool pregame_input = true;
extern "C" void __cdecl horizon_uwp_page_event(const HorizonUwpEvent* event);
extern "C" void __cdecl horizon_uwp_page_shutdown();
extern "C" int __cdecl horizon_uwp_window_closed() { return account_window_closed.load() ? 1 : 0; }
extern "C" int __cdecl horizon_uwp_window_visible() { return account_window_visible.load() ? 1 : 0; }
#endif
static HorizonUwpEvent pending_events[4096];
static uint32_t event_read, event_write;

static void __cdecl queue_event(void*, const HorizonUwpEvent* event) {
#ifdef HORIZON_UWP_ACCOUNT
    // The native startup page owns its input until the game is started.
    if (pregame_input) {
        horizon_uwp_page_event(event);
        if (event->kind >= HORIZON_UWP_KEY_DOWN) return;
    }
#endif
    const uint32_t next = (event_write + 1) % 4096;
    if (next == event_read) event_read = (event_read + 1) % 4096;
    pending_events[event_write] = *event;
    event_write = next;
}
#ifdef HORIZON_UWP_ACCOUNT
extern "C" void __cdecl horizon_uwp_discard_pregame_input() {
    horizon_uwp_page_shutdown();
    // Preserve resize, focus, visibility and close events. Dialog input must not
    // be replayed into the game, where it could activate an unintended action.
    uint32_t read = event_read, write = event_read;
    while (read != event_write) {
        const auto event = pending_events[read];
        read = (read + 1) % 4096;
        if (event.kind < HORIZON_UWP_KEY_DOWN) {
            pending_events[write] = event;
            write = (write + 1) % 4096;
        }
    }
    event_write = write;
    pregame_input = false;
}
#endif
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
extern "C" int __cdecl horizon_uwp_is_local_test() {
    try {
        auto installed = Windows::ApplicationModel::Package::Current->InstalledLocation->Path;
        auto marker = Platform::String::Concat(installed, ref new Platform::String(L"\\horizon-isolated-test.flag"));
        HANDLE h = CreateFile2(marker->Data(), GENERIC_READ, FILE_SHARE_READ, OPEN_EXISTING, nullptr);
        if (h == INVALID_HANDLE_VALUE) return 0;
        CloseHandle(h);
        return 1;
    } catch (...) { return 0; }
}
extern "C" void __cdecl horizon_uwp_report_error(const uint8_t* text, size_t len) {
    if (!text && len != 0) return;
    try {
        auto base = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        SYSTEMTIME now;
        GetSystemTime(&now);
        for (unsigned int attempt = 0; attempt < 100; ++attempt) {
            wchar_t name[128];
            swprintf_s(name, L"\\horizon-error-%04u%02u%02u-%02u%02u%02u-%03u-%u-%u.txt",
                now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond,
                now.wMilliseconds, GetCurrentThreadId(), attempt);
            auto file = Platform::String::Concat(base, ref new Platform::String(name));
            HANDLE h = CreateFile2(file->Data(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_NEW, nullptr);
            if (h == INVALID_HANDLE_VALUE) continue;
            while (len != 0) {
                const DWORD chunk = len > MAXDWORD ? MAXDWORD : static_cast<DWORD>(len);
                DWORD written = 0;
                if (!WriteFile(h, text, chunk, &written, nullptr) || written == 0) break;
                text += written;
                len -= written;
            }
            CloseHandle(h);
            break;
        }
    } catch (...) {}
}
static std::atomic<bool> fatal_done{true};
static Windows::Foundation::IAsyncOperation<Windows::UI::Popups::IUICommand^>^ fatal_dialog;
static void show_fatal(Platform::String^ text) {
    try {
        auto dialog = ref new Windows::UI::Popups::MessageDialog(text, L"Horizon error");
        fatal_dialog = dialog->ShowAsync();
        fatal_dialog->Completed = ref new Windows::Foundation::AsyncOperationCompletedHandler<Windows::UI::Popups::IUICommand^>(
            [](Windows::Foundation::IAsyncOperation<Windows::UI::Popups::IUICommand^>^, Windows::Foundation::AsyncStatus) {
                fatal_done.store(true);
            });
    } catch (...) { fatal_done.store(true); }
}
extern "C" int __cdecl horizon_uwp_is_ui_thread() {
    const DWORD owner = current_window_thread.load(std::memory_order_acquire);
    return owner && owner == GetCurrentThreadId();
}
extern "C" int __cdecl horizon_uwp_fatal_done() { return fatal_done.load() ? 1 : 0; }
extern "C" void __cdecl horizon_uwp_fatal_begin(const uint8_t* text, size_t len) {
    try {
        if (!text || !len || len > 4096 || !current_window) return;
        int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<const char*>(text), static_cast<int>(len), nullptr, 0);
        if (count <= 0 || count > 4096) return;
        wchar_t buffer[4097] = {};
        if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<const char*>(text), static_cast<int>(len), buffer, count)) return;
        auto body = ref new Platform::String(buffer, count);
        bool expected = true;
        if (!fatal_done.compare_exchange_strong(expected, false)) return;
        if (horizon_uwp_is_ui_thread()) show_fatal(body);
        else current_window->Dispatcher->RunAsync(Windows::UI::Core::CoreDispatcherPriority::High,
            ref new Windows::UI::Core::DispatchedHandler([body]() { show_fatal(body); }));
    } catch (...) { fatal_done.store(true); }
}
static void report_native_failure(const char* message) {
    const auto text = reinterpret_cast<const uint8_t*>(message);
    const size_t length = strlen(message);
    horizon_uwp_report_error(text, length);
    horizon_uwp_fatal_begin(text, length);
    try {
        while (!horizon_uwp_fatal_done() && horizon_uwp_is_ui_thread() && current_window)
            current_window->Dispatcher->ProcessEvents(
                Windows::UI::Core::CoreProcessEventsOption::ProcessOneAndAllPending);
    } catch (...) {}
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
        try {
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
#ifdef HORIZON_UWP_ACCOUNT
        // The native account dialog needs an active CoreWindow before the game event loop starts.
        window_->Activate();
#endif
        const int32_t result = horizon_uwp_run(reinterpret_cast<const uint16_t*>(resources->Data()), resources->Length(),
                                               reinterpret_cast<const uint16_t*>(game->Data()), game->Length());
        // The Rust bridge reports an error before returning a failure.
        (void)result;
        } catch (Platform::Exception^ error) {
            char message[160];
            sprintf_s(message, "Horizon encountered a Windows error (0x%08X). A diagnostic was saved in app storage.",
                static_cast<unsigned>(error->HResult));
            report_native_failure(message);
        } catch (...) {
            report_native_failure("Horizon encountered a native error. A diagnostic was saved in app storage.");
        }
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
    void Closed(CoreWindow^, CoreWindowEventArgs^) { closed_ = true;
#ifdef HORIZON_UWP_ACCOUNT
        account_window_closed.store(true);
#endif
    }
    void VisibilityChanged(CoreWindow^, VisibilityChangedEventArgs^ args) { visible_ = args->Visible;
#ifdef HORIZON_UWP_ACCOUNT
        account_window_visible.store(args->Visible);
#endif
    }
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
#ifdef HORIZON_UWP_GAME_BRIDGE
    try {
#endif
    CoreApplication::Run(ref new AppViewSource());
#ifdef HORIZON_UWP_GAME_BRIDGE
    } catch (Platform::Exception^ error) {
        char message[160];
        sprintf_s(message, "Horizon failed to initialize (0x%08X). A diagnostic was saved in app storage.",
            static_cast<unsigned>(error->HResult));
        report_native_failure(message);
        return 1;
    } catch (...) {
        report_native_failure("Horizon failed to initialize. A diagnostic was saved in app storage.");
        return 1;
    }
#endif
    return 0;
}

