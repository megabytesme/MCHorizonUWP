#if defined(_MSC_VER) && _MSC_VER >= 1940 && defined(WINAPI_FAMILY)
extern "C" __declspec(dllimport) char* __cdecl getenv(const char*);
extern "C" __declspec(dllimport) int __cdecl system(const char*);
#endif
#include <windows.h>
#include <windows.storage.h>
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <algorithm>
#include <atomic>
#include <mutex>
#include "Dx11Renderer.h"
#ifdef HORIZON_UWP_GAME_BRIDGE
#include "../window-bridge/CoreWindowEvents.h"
#include <wrl/client.h>
#include <windows.applicationmodel.h>
#include <windows.foundation.metadata.h>
#include <windows.graphics.display.h>
#include <windows.system.h>
#include <windows.system.threading.h>
#include <windows.ui.viewmanagement.h>
#include <windows.ui.popups.h>

static Windows::UI::Core::CoreWindow^ current_window;
static std::atomic<DWORD> current_window_thread{0};
#ifdef HORIZON_UWP_ACCOUNT
static std::atomic<bool> account_window_closed{false};
static std::atomic<bool> account_window_visible{false};
#ifdef HORIZON_UWP_TOUCH_MOD
extern "C" int __cdecl horizon_uwp_gyro_enable(int enabled);
extern "C" void __cdecl horizon_uwp_gyro_set_visible(int visible);
#endif
#ifdef HORIZON_UWP_XAML_SHELL
static Windows::UI::Core::CoreWindow^ primary_window;
static Windows::UI::Core::CoreDispatcher^ primary_dispatcher;
static DWORD primary_window_thread = 0;
// Keep the original CoreWindow COM identity alive for raw renderer APIs.
// EGL's WinRT surface creation consumes the actual CoreWindow pointer and
// cannot use the IAgileReference proxy returned by RoGetAgileReference.
static Microsoft::WRL::ComPtr<IUnknown> primary_window_native;
// ANGLE uses the existing XAML SwapChainPanel as its EGL window target. Keep
// the panel's original WinRT identity alive; apartment proxies aren't valid
// native EGL window handles.
static Microsoft::WRL::ComPtr<IUnknown> game_surface_native;
static horizon_uwp::CoreWindowEvents^ active_game_events;
static Windows::UI::ViewManagement::ApplicationView^ game_application_view;
static bool game_phone = false;
static bool game_was_full_screen = false;
static bool game_page_hidden = false;
static bool game_gyro_visible = false;
static std::atomic<bool> game_running{false};
static std::atomic<bool> session_preparation_running{false};
#endif
static std::atomic<bool> pregame_input{true};
extern "C" void __cdecl horizon_uwp_page_event(const HorizonUwpEvent* event);
extern "C" void __cdecl horizon_uwp_page_shutdown();
#ifdef HORIZON_UWP_XAML_SHELL
extern "C" void __cdecl horizon_uwp_resume_pregame_input();
extern "C" int __cdecl horizon_uwp_page_selected_renderer();
#endif
extern "C" int __cdecl horizon_uwp_window_closed() { return account_window_closed.load() ? 1 : 0; }
extern "C" int __cdecl horizon_uwp_window_visible() { return account_window_visible.load() ? 1 : 0; }
#ifdef HORIZON_UWP_XAML_SHELL
extern "C" void __cdecl horizon_uwp_attach_shell(Windows::UI::Core::CoreWindow^ window) {
    current_window = window;
    current_window_thread.store(GetCurrentThreadId(), std::memory_order_release);
#ifdef HORIZON_UWP_XAML_SHELL
    primary_window = window;
    primary_dispatcher = window->Dispatcher;
    primary_window_thread = GetCurrentThreadId();
    primary_window_native = reinterpret_cast<IUnknown*>(window);
#endif
    account_window_closed.store(false);
    account_window_visible.store(true);
    window->Closed += ref new Windows::Foundation::TypedEventHandler<Windows::UI::Core::CoreWindow^, Windows::UI::Core::CoreWindowEventArgs^>(
        [](Windows::UI::Core::CoreWindow^, Windows::UI::Core::CoreWindowEventArgs^) {
            account_window_closed.store(true);
#ifdef HORIZON_UWP_TOUCH_MOD
            horizon_uwp_gyro_set_visible(0);
            horizon_uwp_gyro_enable(0);
#endif
        });
    window->VisibilityChanged += ref new Windows::Foundation::TypedEventHandler<Windows::UI::Core::CoreWindow^, Windows::UI::Core::VisibilityChangedEventArgs^>(
        [](Windows::UI::Core::CoreWindow^, Windows::UI::Core::VisibilityChangedEventArgs^ args) {
            account_window_visible.store(args->Visible);
#ifdef HORIZON_UWP_TOUCH_MOD
            horizon_uwp_gyro_set_visible(args->Visible ? 1 : 0);
#endif
        });
}
#endif
extern "C" void __cdecl horizon_uwp_register_game_surface(void* surface) {
#ifdef HORIZON_UWP_XAML_SHELL
    if (surface) game_surface_native = reinterpret_cast<IUnknown*>(surface);
#else
    (void)surface;
#endif
}
#endif
static HorizonUwpEvent pending_events[4096];
static uint32_t event_read, event_write;
static std::mutex event_queue_mutex;
static uint32_t cached_width, cached_height;
static double cached_scale = 1.0;
static uint32_t game_surface_width, game_surface_height;
static double game_surface_scale_x = 1.0, game_surface_scale_y = 1.0;
static double game_surface_input_origin_y;
static std::atomic<bool> game_surface_active{false};

#ifdef HORIZON_UWP_DISPLAY_DIAGNOSTICS
static std::atomic<uint32_t> display_pointer_samples{0};
static void display_trace(const char* format, ...) {
    try {
        char line[512];
        va_list args;
        va_start(args, format);
        const int length = vsnprintf_s(line, sizeof(line), _TRUNCATE, format, args);
        va_end(args);
        if (length <= 0) return;
        auto folder = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        auto path = Platform::String::Concat(folder, L"\\horizon-display-trace.log");
        HANDLE file = CreateFile2(path->Data(), FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE, OPEN_ALWAYS, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        DWORD written = 0;
        WriteFile(file, line, static_cast<DWORD>(length), &written, nullptr);
        CloseHandle(file);
    } catch (...) {}
}
#endif

static void __cdecl queue_event(void*, const HorizonUwpEvent* event) {
    if (!event) return;
    const HorizonUwpEvent raw = *event;
    HorizonUwpEvent adjusted = *event;
    {
        std::lock_guard<std::mutex> lock(event_queue_mutex);
        if (game_surface_active.load(std::memory_order_acquire) && game_surface_width && game_surface_height) {
            if (adjusted.kind == HORIZON_UWP_RESIZE) {
                adjusted.width = static_cast<float>(game_surface_width);
                adjusted.height = static_cast<float>(game_surface_height);
            } else if ((adjusted.kind >= HORIZON_UWP_POINTER_MOVE &&
                        adjusted.kind <= HORIZON_UWP_POINTER_WHEEL) ||
                       adjusted.kind == HORIZON_UWP_POINTER_CANCEL) {
                const double event_scale = adjusted.scale_factor > 0.0f ? adjusted.scale_factor : 1.0;
                adjusted.x = static_cast<float>(adjusted.x * game_surface_scale_x / event_scale);
                adjusted.y = static_cast<float>(adjusted.y * game_surface_scale_y / event_scale - game_surface_input_origin_y);
            }
            adjusted.scale_factor = static_cast<float>(game_surface_scale_x);
        }
    }
#ifdef HORIZON_UWP_DISPLAY_DIAGNOSTICS
    if (raw.kind == HORIZON_UWP_RESIZE) {
        uint32_t width = 0, height = 0;
        double scale_x = 1.0, scale_y = 1.0;
        { std::lock_guard<std::mutex> lock(event_queue_mutex);
          width = game_surface_width; height = game_surface_height;
          scale_x = game_surface_scale_x; scale_y = game_surface_scale_y; }
        display_trace("resize raw=%.0fx%.0f event_scale=%.4f normalized=%.0fx%.0f panel=%ux%u panel_scale=%.4fx%.4f active=%d\r\n",
            raw.width, raw.height, raw.scale_factor, adjusted.width, adjusted.height,
            width, height, scale_x, scale_y, game_surface_active.load() ? 1 : 0);
    } else if ((raw.kind >= HORIZON_UWP_POINTER_MOVE && raw.kind <= HORIZON_UWP_POINTER_WHEEL) ||
               raw.kind == HORIZON_UWP_POINTER_CANCEL) {
        const uint32_t sample = display_pointer_samples.fetch_add(1) + 1;
        if (sample <= 24 || sample % 100 == 0) {
            uint32_t width = 0, height = 0;
            double scale_x = 1.0, scale_y = 1.0;
            { std::lock_guard<std::mutex> lock(event_queue_mutex);
              width = game_surface_width; height = game_surface_height;
              scale_x = game_surface_scale_x; scale_y = game_surface_scale_y; }
            display_trace("pointer sample=%u kind=%u raw=%.2f,%.2f event_scale=%.4f normalized=%.2f,%.2f panel_scale=%.4f,%.4f panel=%ux%u\r\n",
                sample, raw.kind, raw.x, raw.y, raw.scale_factor, adjusted.x, adjusted.y,
                scale_x, scale_y, width, height);
        }
    }
#endif
#ifdef HORIZON_UWP_ACCOUNT
    // The native startup page owns its input until the game is started.
    if (pregame_input.load(std::memory_order_acquire)) {
        horizon_uwp_page_event(&adjusted);
        if (adjusted.kind >= HORIZON_UWP_KEY_DOWN) return;
    }
#endif
    std::lock_guard<std::mutex> lock(event_queue_mutex);
    if (adjusted.kind == HORIZON_UWP_RESIZE) {
        cached_width = static_cast<uint32_t>((std::max)(0.0f, adjusted.width));
        cached_height = static_cast<uint32_t>((std::max)(0.0f, adjusted.height));
        cached_scale = adjusted.scale_factor > 0.0f ? adjusted.scale_factor : 1.0;
    }
    const uint32_t next = (event_write + 1) % 4096;
    if (next == event_read) event_read = (event_read + 1) % 4096;
    pending_events[event_write] = adjusted;
    event_write = next;
}

extern "C" void __cdecl horizon_uwp_set_game_surface_metrics(
    uint32_t width, uint32_t height, double scale_x, double scale_y, double input_origin_y) {
    if (!width || !height || scale_x <= 0.0 || scale_y <= 0.0) return;
    {
        std::lock_guard<std::mutex> lock(event_queue_mutex);
        game_surface_width = width;
        game_surface_height = height;
        game_surface_scale_x = scale_x;
        game_surface_scale_y = scale_y;
        game_surface_input_origin_y = (std::max)(0.0, input_origin_y);
    }
    HorizonUwpEvent resize{};
    resize.kind = HORIZON_UWP_RESIZE;
    resize.width = static_cast<float>(width);
    resize.height = static_cast<float>(height);
    resize.scale_factor = static_cast<float>(scale_x);
    queue_event(nullptr, &resize);
}

#ifdef HORIZON_UWP_DISPLAY_DIAGNOSTICS
extern "C" void __cdecl horizon_uwp_trace_game_surface(
    double actual_width, double actual_height, double scale_x, double scale_y,
    uint32_t pixel_width, uint32_t pixel_height) {
    double bounds_width = 0.0, bounds_height = 0.0, logical_dpi = 0.0;
    try {
        if (current_window) {
            bounds_width = current_window->Bounds.Width;
            bounds_height = current_window->Bounds.Height;
            logical_dpi = Windows::Graphics::Display::DisplayInformation::GetForCurrentView()->LogicalDpi;
        }
    } catch (...) {}
    display_trace("panel actual=%.2fx%.2f composition=%.4fx%.4f pixels=%ux%u corewindow=%.2fx%.2f logical_dpi=%.2f\r\n",
        actual_width, actual_height, scale_x, scale_y, pixel_width, pixel_height,
        bounds_width, bounds_height, logical_dpi);
}

extern "C" void __cdecl horizon_uwp_trace_game_swapchain(uint32_t width, uint32_t height) {
    display_trace("swapchain buffers=%ux%u\r\n", width, height);
}
#endif

extern "C" void __cdecl horizon_uwp_set_game_surface_active(int active) {
    game_surface_active.store(active != 0, std::memory_order_release);
#ifdef HORIZON_UWP_DISPLAY_DIAGNOSTICS
    display_trace("surface active=%d\r\n", active != 0 ? 1 : 0);
#endif
}
#ifdef HORIZON_UWP_ACCOUNT
extern "C" void __cdecl horizon_uwp_discard_pregame_input() {
    horizon_uwp_page_shutdown();
    // Preserve resize, focus, visibility and close events. Dialog input must not
    // be replayed into the game, where it could activate an unintended action.
    std::lock_guard<std::mutex> lock(event_queue_mutex);
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
    pregame_input.store(false, std::memory_order_release);
}
#ifdef HORIZON_UWP_XAML_SHELL
extern "C" void __cdecl horizon_uwp_resume_pregame_input() {
    std::lock_guard<std::mutex> lock(event_queue_mutex);
    event_read = event_write = 0;
    pregame_input.store(true, std::memory_order_release);
}
#endif
#endif

#ifdef HORIZON_UWP_GAME_BRIDGE
static Platform::String^ resolve_resources(Platform::String^ local_path) {
    auto resources = Platform::String::Concat(local_path, ref new Platform::String(L"\\resources"));
    auto manifest = Platform::String::Concat(resources, ref new Platform::String(L"\\horizon-resources.json"));
    HANDLE local_manifest = CreateFile2(manifest->Data(), GENERIC_READ,
                                        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                        OPEN_EXISTING, nullptr);
    if (local_manifest == INVALID_HANDLE_VALUE) {
        auto installed = Windows::ApplicationModel::Package::Current->InstalledLocation->Path;
        resources = Platform::String::Concat(installed, ref new Platform::String(L"\\resources"));
        Platform::String^ framework_resources = nullptr;
        auto dependencies = Windows::ApplicationModel::Package::Current->Dependencies;
        auto publisher = Windows::ApplicationModel::Package::Current->Id->Publisher;
        for (unsigned int index = 0; index < dependencies->Size; ++index) {
            auto dependency = dependencies->GetAt(index);
            auto name = dependency->Id->Name;
            constexpr wchar_t prefix[] = L"Horizon.Asset.";
            if (wcsncmp(name->Data(), prefix, _countof(prefix) - 1) != 0) continue;
            if (wcscmp(dependency->Id->Publisher->Data(), publisher->Data()) != 0) continue;
            if (framework_resources != nullptr) throw ref new Platform::FailureException();
            framework_resources = Platform::String::Concat(
                dependency->InstalledLocation->Path, ref new Platform::String(L"\\resources"));
        }
        if (framework_resources != nullptr) resources = framework_resources;
    } else {
        CloseHandle(local_manifest);
    }
    return resources;
}
#ifdef HORIZON_UWP_XAML_SHELL
extern "C" int __cdecl horizon_uwp_cache_vanilla_icon() {
    try {
        auto local = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        auto resources = resolve_resources(local);
        auto source = Platform::String::Concat(resources, L"\\assets\\minecraft\\textures\\misc\\unknown_server.png");
        auto target = Platform::String::Concat(local, L"\\horizon-default-icon.png");
        return SUCCEEDED(CopyFile2(source->Data(), target->Data(), nullptr)) ? 1 : 0;
    } catch (Platform::Exception^) { return 0; }
}
#endif

#endif

extern "C" void* __cdecl horizon_uwp_window_handle() {
#ifdef HORIZON_UWP_XAML_SHELL
    // Rust's patched UWP environment is process-local and is not the CRT
    // environment read by getenv. The shell owns the authoritative selection.
    if (horizon_uwp_page_selected_renderer() == 1 && game_surface_native)
        return game_surface_native.Get();
    const DWORD owner = current_window_thread.load(std::memory_order_acquire);
    if (!owner || !current_window) return nullptr;
    if (GetCurrentThreadId() == owner) return reinterpret_cast<void*>(current_window);
    // WGPU's EGL WinRT backend passes this value straight to EGL. A COM
    // apartment proxy looks valid as IUnknown but is not the native XAML
    // surface handle ANGLE expects. The UI host retains this identity;
    // game-window operations still marshal through the XAML dispatcher.
    return primary_window_native.Get();
#else
    // CoreWindow must only be handed out on its owning UI thread.
    const DWORD owner = current_window_thread.load(std::memory_order_acquire);
    if (!owner || GetCurrentThreadId() != owner) return nullptr;
    return reinterpret_cast<void*>(current_window);
#endif
}

// ANGLE otherwise sizes a SwapChainPanel in XAML's logical pixels, while the
// game renders at physical-pixel resolution. Its property set lets ANGLE
// create and scale a swap chain that matches the game framebuffer.
extern "C" int32_t __cdecl horizon_uwp_angle_window_properties(
    void* window, uint32_t width, uint32_t height, void** result) {
    if (!result) return E_POINTER;
    *result = nullptr;
#ifdef HORIZON_UWP_XAML_SHELL
    if (!game_surface_native || window != game_surface_native.Get()) return S_FALSE;
    if (!width || !height) return E_INVALIDARG;
    try {
        auto properties = ref new Windows::Foundation::Collections::PropertySet();
        properties->Insert(L"EGLNativeWindowTypeProperty", reinterpret_cast<Platform::Object^>(window));
        properties->Insert(L"EGLRenderSurfaceSizeProperty",
            Windows::Foundation::PropertyValue::CreateSize(
                Windows::Foundation::Size(static_cast<float>(width), static_cast<float>(height))));
        auto native = reinterpret_cast<IUnknown*>(properties);
        native->AddRef();
        *result = native;
        return S_OK;
    } catch (Platform::Exception^ error) {
        return error->HResult;
    }
#else
    (void)window; (void)width; (void)height;
    return S_FALSE;
#endif
}

extern "C" void __cdecl horizon_uwp_release_angle_window_properties(void* properties) {
    if (properties) reinterpret_cast<IUnknown*>(properties)->Release();
}

#if defined(HORIZON_UWP_ACCOUNT) && defined(HORIZON_UWP_XAML_SHELL)
extern "C" void __cdecl horizon_uwp_page_game_menu_shown();
#endif
extern "C" void __cdecl horizon_uwp_game_menu_shown() {
#if defined(HORIZON_UWP_ACCOUNT) && defined(HORIZON_UWP_XAML_SHELL)
    horizon_uwp_page_game_menu_shown();
#endif
}
extern "C" void __cdecl horizon_uwp_pump_events() {
    // The XAML account state machine runs on a worker. Only the owning UI
    // thread may pump a CoreWindow dispatcher.
#ifdef HORIZON_UWP_XAML_SHELL
    return; // XAML owns this dispatcher's outer event loop.
#else
    if (!current_window || current_window_thread.load(std::memory_order_acquire) != GetCurrentThreadId()) return;
    current_window->Dispatcher->ProcessEvents(Windows::UI::Core::CoreProcessEventsOption::ProcessAllIfPresent);
#endif
}
extern "C" int __cdecl horizon_uwp_pop_event(HorizonUwpEvent* event) {
    if (!event) return 0;
    std::lock_guard<std::mutex> lock(event_queue_mutex);
    if (event_read == event_write) return 0;
    *event = pending_events[event_read];
    event_read = (event_read + 1) % 4096;
    return 1;
}
extern "C" void __cdecl horizon_uwp_get_metrics(uint32_t* width, uint32_t* height, double* scale) {
    if (!width || !height || !scale) return;
    std::lock_guard<std::mutex> lock(event_queue_mutex);
    if (game_surface_active.load(std::memory_order_acquire) && game_surface_width && game_surface_height) {
        *width = game_surface_width;
        *height = game_surface_height;
        *scale = game_surface_scale_x;
    } else {
        *width = cached_width;
        *height = cached_height;
        *scale = cached_scale;
    }
}
extern "C" void __cdecl horizon_uwp_get_surface_metrics(
    uint32_t* width, uint32_t* height, double* scale_x, double* scale_y) {
    if (!width || !height || !scale_x || !scale_y) return;
    std::lock_guard<std::mutex> lock(event_queue_mutex);
    if (game_surface_active.load(std::memory_order_acquire) && game_surface_width && game_surface_height) {
        *width = game_surface_width;
        *height = game_surface_height;
        *scale_x = game_surface_scale_x;
        *scale_y = game_surface_scale_y;
    } else {
        *width = cached_width;
        *height = cached_height;
        *scale_x = cached_scale;
        *scale_y = cached_scale;
    }
}
extern "C" void __cdecl horizon_uwp_set_cursor_visible(int visible) {
    if (!current_window) return;
    auto set_cursor = [visible]() {
        if (current_window) current_window->PointerCursor = visible
            ? ref new Windows::UI::Core::CoreCursor(Windows::UI::Core::CoreCursorType::Arrow, 0)
            : nullptr;
    };
    if (current_window_thread.load(std::memory_order_acquire) == GetCurrentThreadId()) set_cursor();
#ifdef HORIZON_UWP_XAML_SHELL
    else if (primary_dispatcher) primary_dispatcher->RunAsync(
        Windows::UI::Core::CoreDispatcherPriority::Normal,
        ref new Windows::UI::Core::DispatchedHandler(set_cursor));
#endif
}
extern "C" void __cdecl horizon_uwp_set_ime_allowed(int allowed) {
    auto set_ime = [allowed]() {
        try {
            auto pane = Windows::UI::ViewManagement::InputPane::GetForCurrentView();
            if (pane) { if (allowed) pane->TryShow(); else pane->TryHide(); }
        } catch (Platform::Exception^) { /* InputPane is optional. */ }
    };
    if (current_window_thread.load(std::memory_order_acquire) == GetCurrentThreadId()) set_ime();
#ifdef HORIZON_UWP_XAML_SHELL
    else if (primary_dispatcher) primary_dispatcher->RunAsync(
        Windows::UI::Core::CoreDispatcherPriority::Normal,
        ref new Windows::UI::Core::DispatchedHandler(set_ime));
#endif
}

extern "C" int __cdecl horizon_uwp_set_cursor_position(double x, double y) {
    if (!current_window) return 0;
    double scale = 1.0;
    double origin_y = 0.0;
    { std::lock_guard<std::mutex> lock(event_queue_mutex); scale = cached_scale; origin_y = game_surface_input_origin_y; }
    auto set_position = [x, y, scale, origin_y]() {
        try { if (current_window) current_window->PointerPosition = Windows::Foundation::Point(
            static_cast<float>(x / scale), static_cast<float>((y + origin_y) / scale)); }
        catch (Platform::Exception^) {}
    };
    if (current_window_thread.load(std::memory_order_acquire) == GetCurrentThreadId()) {
        set_position();
        return 1;
    }
#ifdef HORIZON_UWP_XAML_SHELL
    if (primary_dispatcher) {
        primary_dispatcher->RunAsync(Windows::UI::Core::CoreDispatcherPriority::Normal,
            ref new Windows::UI::Core::DispatchedHandler(set_position));
        return 1;
    }
#endif
    return 0;
}
extern "C" int32_t __cdecl horizon_uwp_run(const uint16_t*, size_t, const uint16_t*, size_t);
#ifdef HORIZON_UWP_XAML_SHELL
extern "C" int32_t __cdecl horizon_uwp_run_prepared(
    const uint16_t*, size_t, const uint16_t*, size_t, uint32_t);
extern "C" int __cdecl horizon_uwp_page_selected_renderer();
#ifdef HORIZON_UWP_NATIVE_D3D11
extern "C" int32_t __cdecl horizon_uwp_prepare_native_renderer();
#endif
extern "C" int __cdecl horizon_uwp_xaml_start();
extern "C" void __cdecl horizon_uwp_page_refresh_tile();
extern "C" int32_t __cdecl horizon_uwp_page_show_game(int);
extern "C" int __cdecl horizon_uwp_page_exit_after_game();
extern "C" int __cdecl horizon_uwp_prepare(const uint16_t*, size_t);
extern "C" void __cdecl horizon_uwp_open_game_view();
extern "C" void __cdecl horizon_uwp_trace_shell_event(const char* event);
extern "C" size_t __cdecl horizon_uwp_page_switch_error(uint8_t*, size_t);
#endif
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
extern "C" int __cdecl horizon_uwp_memory_usage(uint64_t* used, uint64_t* limit) {
    if (!used || !limit) return 0;
    try {
        *used = Windows::System::MemoryManager::AppMemoryUsage;
        *limit = Windows::System::MemoryManager::AppMemoryUsageLimit;
        return 1;
    } catch (Platform::Exception^) { return 0; }
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
using HorizonUwpUiCallback = void(__cdecl*)(void*);
extern "C" int32_t __cdecl horizon_uwp_dispatch_ui_sync(
    HorizonUwpUiCallback callback, void* context) {
#ifdef HORIZON_UWP_XAML_SHELL
    if (!callback || !primary_dispatcher) return E_POINTER;
    auto invoke = [callback, context]() -> int32_t {
        try { callback(context); return S_OK; }
        catch (Platform::Exception^ error) { return error->HResult; }
        catch (...) { return E_FAIL; }
    };
    if (GetCurrentThreadId() == primary_window_thread) return invoke();
    HANDLE completed = CreateEventEx(nullptr, nullptr, CREATE_EVENT_MANUAL_RESET, EVENT_ALL_ACCESS);
    if (!completed) return HRESULT_FROM_WIN32(GetLastError());
    std::atomic<int32_t> result{S_OK};
    try {
        primary_dispatcher->RunAsync(Windows::UI::Core::CoreDispatcherPriority::High,
            ref new Windows::UI::Core::DispatchedHandler([invoke, completed, &result]() {
                result.store(invoke(), std::memory_order_release);
                SetEvent(completed);
            }));
    } catch (Platform::Exception^ error) {
        CloseHandle(completed);
        return error->HResult;
    } catch (...) {
        CloseHandle(completed);
        return E_FAIL;
    }
    const DWORD wait = WaitForSingleObject(completed, INFINITE);
    const int32_t status = result.load(std::memory_order_acquire);
    CloseHandle(completed);
    return wait == WAIT_OBJECT_0 ? status : HRESULT_FROM_WIN32(GetLastError());
#else
    if (!callback) return E_POINTER;
    try { callback(context); return S_OK; }
    catch (...) { return E_FAIL; }
#endif
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
#ifdef HORIZON_UWP_XAML_SHELL
        else if (primary_dispatcher) primary_dispatcher->RunAsync(Windows::UI::Core::CoreDispatcherPriority::High,
            ref new Windows::UI::Core::DispatchedHandler([body]() { show_fatal(body); }));
#else
        else current_window->Dispatcher->RunAsync(Windows::UI::Core::CoreDispatcherPriority::High,
            ref new Windows::UI::Core::DispatchedHandler([body]() { show_fatal(body); }));
#endif
    } catch (...) { fatal_done.store(true); }
}
static void report_native_failure(const char* message) {
    const auto text = reinterpret_cast<const uint8_t*>(message);
    const size_t length = strlen(message);
    horizon_uwp_report_error(text, length);
    // ShowAsync completes when the owning XAML dispatcher resumes naturally.
    // Pumping ProcessEvents here can occur from inside a RunAsync callback and
    // throws RPC_E_CANTCALLOUT_ININPUTSYNCCALL, masking the original failure.
    horizon_uwp_fatal_begin(text, length);
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
#ifdef HORIZON_UWP_XAML_SHELL
        if (primary_window && window_ != primary_window) {
            current_window = primary_window;
            current_window_thread.store(primary_window_thread, std::memory_order_release);
            account_window_closed.store(false);
            account_window_visible.store(true);
        } else {
            current_window_thread.store(0, std::memory_order_release);
            current_window = nullptr;
        }
#else
        current_window_thread.store(0, std::memory_order_release);
        current_window = nullptr;
#endif
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
        auto resources = resolve_resources(local_path);
        auto game = Platform::String::Concat(local_path, ref new Platform::String(L"\\game"));
#ifdef HORIZON_UWP_ACCOUNT
        // The native account dialog needs an active CoreWindow before the game event loop starts.
        window_->Activate();
#endif
#ifdef HORIZON_UWP_XAML_SHELL
        const int32_t result = horizon_uwp_run_prepared(reinterpret_cast<const uint16_t*>(resources->Data()), resources->Length(),
                                               reinterpret_cast<const uint16_t*>(game->Data()), game->Length(),
                                               static_cast<uint32_t>(horizon_uwp_page_selected_renderer()));
#else
        const int32_t result = horizon_uwp_run(reinterpret_cast<const uint16_t*>(resources->Data()), resources->Length(),
                                               reinterpret_cast<const uint16_t*>(game->Data()), game->Length());
#endif
        // The Rust bridge reports an error before returning a failure.
        (void)result;
        horizon_uwp_page_refresh_tile();
        } catch (Platform::Exception^ error) {
            char message[160];
            sprintf_s(message, "Horizon encountered a Windows error (0x%08X). A diagnostic was saved in app storage.",
                static_cast<unsigned>(error->HResult));
            report_native_failure(message);
        } catch (...) {
            report_native_failure("Horizon encountered a native error. A diagnostic was saved in app storage.");
        }
#ifdef HORIZON_UWP_XAML_SHELL
        Windows::ApplicationModel::Core::CoreApplication::Exit();
#endif
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
#ifdef HORIZON_UWP_TOUCH_MOD
        horizon_uwp_gyro_set_visible(0);
        horizon_uwp_gyro_enable(0);
#endif
#ifdef HORIZON_UWP_ACCOUNT
        account_window_closed.store(true);
#endif
    }
    void VisibilityChanged(CoreWindow^, VisibilityChangedEventArgs^ args) { visible_ = args->Visible;
#ifdef HORIZON_UWP_TOUCH_MOD
        horizon_uwp_gyro_set_visible(args->Visible ? 1 : 0);
#endif
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

#ifdef HORIZON_UWP_XAML_SHELL
static void prepare_next_game_session() {
    if (horizon_uwp_page_exit_after_game() != 0) return;
    bool expected = false;
    if (!session_preparation_running.compare_exchange_strong(expected, true, std::memory_order_acq_rel)) return;
    horizon_uwp_trace_shell_event("next game-session preparation queued");
    try {
        Windows::System::Threading::ThreadPool::RunAsync(
            ref new Windows::System::Threading::WorkItemHandler([](Windows::Foundation::IAsyncAction^) {
                int result = -1;
                horizon_uwp_trace_shell_event("next game-session preparation started");
                try {
                    auto local = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
                    auto game = Platform::String::Concat(local, L"\\game");
                    result = horizon_uwp_prepare(
                        reinterpret_cast<const uint16_t*>(game->Data()), game->Length());
                } catch (Platform::Exception^ error) {
                    char message[192];
                    sprintf_s(message, "Horizon could not prepare the next game session (0x%08X). A diagnostic was saved in app storage.",
                        static_cast<unsigned>(error->HResult));
                    report_native_failure(message);
                } catch (...) {
                    report_native_failure("Horizon could not prepare the next game session. A diagnostic was saved in app storage.");
                }
                char event[96];
                sprintf_s(event, "next game-session preparation returned %d", result);
                horizon_uwp_trace_shell_event(event);
                if (result == 1 && primary_dispatcher) {
                    primary_dispatcher->RunAsync(
                        Windows::UI::Core::CoreDispatcherPriority::Normal,
                        ref new Windows::UI::Core::DispatchedHandler([]() {
                            session_preparation_running.store(false, std::memory_order_release);
                            if (horizon_uwp_page_exit_after_game() == 0) {
                                horizon_uwp_trace_shell_event("prepared session accepted; opening game view");
                                horizon_uwp_open_game_view();
                            }
                        }));
                } else {
                    session_preparation_running.store(false, std::memory_order_release);
                }
            }),
            Windows::System::Threading::WorkItemPriority::Normal,
            Windows::System::Threading::WorkItemOptions::TimeSliced);
    } catch (...) {
        session_preparation_running.store(false, std::memory_order_release);
        report_native_failure("Horizon could not start the next game-session worker.");
    }
}

static void finish_game_in_primary_view() {
    if (GetCurrentThreadId() != primary_window_thread) {
        if (primary_dispatcher) primary_dispatcher->RunAsync(
            Windows::UI::Core::CoreDispatcherPriority::Normal,
            ref new Windows::UI::Core::DispatchedHandler([]() { finish_game_in_primary_view(); }));
        return;
    }
#ifdef HORIZON_UWP_TOUCH_MOD
    if (game_gyro_visible) horizon_uwp_gyro_set_visible(0);
    horizon_uwp_gyro_enable(0);
    game_gyro_visible = false;
#endif
    const bool exit_after_game = horizon_uwp_page_exit_after_game() != 0;
    horizon_uwp_trace_shell_event(exit_after_game
        ? "game ended; quit behavior is close app"
        : "game ended; returning to Horizon home");
    if (active_game_events) {
        delete active_game_events;
        active_game_events = nullptr;
    }
    horizon_uwp_resume_pregame_input();
    if (game_phone && !game_was_full_screen && game_application_view) {
        try { game_application_view->ExitFullScreenMode(); }
        catch (Platform::Exception^) {}
    }
    if (game_page_hidden && !account_window_closed.load()) {
        const int32_t page_result = horizon_uwp_page_show_game(exit_after_game ? -1 : 0);
        if (page_result < 0)
            report_native_failure("Horizon could not restore the pre-game page after the game stopped.");
    }
    game_page_hidden = false;
    game_phone = false;
    game_was_full_screen = false;
    game_application_view = nullptr;
    game_running.store(false, std::memory_order_release);
    if (exit_after_game) Windows::ApplicationModel::Core::CoreApplication::Exit();
    else prepare_next_game_session();
}

static void run_game_in_primary_view() {
    if (!primary_window || GetCurrentThreadId() != primary_window_thread) {
        report_native_failure("Horizon could not start the game on its UWP view thread.");
        return;
    }
    bool expected = false;
    if (!game_running.compare_exchange_strong(expected, true, std::memory_order_acq_rel)) return;

    auto window = primary_window;
    const char* stage = "loading the game resources";
    Windows::UI::ViewManagement::ApplicationView^ application_view = nullptr;
    bool phone = false;
    bool was_full_screen = false;
    bool page_hidden = false;
    bool worker_started = false;
    horizon_uwp::CoreWindowEvents^ events = nullptr;
#ifdef HORIZON_UWP_TOUCH_MOD
    bool gyro_visible = false;
#endif
    try {
        auto local_path = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        auto resources = resolve_resources(local_path);
        auto game = Platform::String::Concat(local_path, ref new Platform::String(L"\\game"));
        const unsigned renderer = static_cast<unsigned>(horizon_uwp_page_selected_renderer());
        char start_event[96];
        sprintf_s(start_event, "starting game view with renderer %u", renderer);
        horizon_uwp_trace_shell_event(start_event);
        stage = "checking the active UWP view";
        application_view = Windows::UI::ViewManagement::ApplicationView::GetForCurrentView();
        was_full_screen = application_view->IsFullScreenMode;
        phone = Windows::Foundation::Metadata::ApiInformation::IsApiContractPresent(
            L"Windows.Phone.PhoneContract", 1);
        if (phone && !was_full_screen) {
            try { application_view->TryEnterFullScreenMode(); }
            catch (Platform::Exception^) {}
        }

        // Subscribe while XAML still owns the CoreWindow. Older Windows 10
        // Mobile builds can reject CoreWindow input event registration after
        // Window::Content is cleared, even though the CoreWindow is unchanged.
        stage = "attaching game input to the existing CoreWindow";
        events = ref new horizon_uwp::CoreWindowEvents(window, queue_event, nullptr);

        stage = "sending the initial game size";
        events->EmitInitialSize();

        stage = "showing the game surface over the launcher page";
        const int32_t page_result = horizon_uwp_page_show_game(1);
        if (page_result < 0)
            throw Platform::Exception::CreateException(page_result);
        page_hidden = true;

        if (renderer == 0) {
#ifdef HORIZON_UWP_NATIVE_D3D11
            // Bind the native composition swap chain to the existing XAML panel.
            stage = "creating the Direct3D 11 composition surface on the UWP UI thread";
            const int32_t renderer_result = horizon_uwp_prepare_native_renderer();
            if (renderer_result < 0)
                throw Platform::Exception::CreateException(renderer_result);
#else
            throw Platform::Exception::CreateException(E_NOTIMPL);
#endif
        }

#ifdef HORIZON_UWP_TOUCH_MOD
        stage = "starting device gyroscope input";
        horizon_uwp_gyro_set_visible(1);
        gyro_visible = true;
#endif
        // XAML owns the CoreWindow dispatcher. Run Horizon on a render worker
        // so its winit loop can poll queued input while XAML continues to
        // dispatch CoreWindow events without a nested ProcessEvents call.
        active_game_events = events;
        events = nullptr;
        game_application_view = application_view;
        game_phone = phone;
        game_was_full_screen = was_full_screen;
        game_page_hidden = page_hidden;
#ifdef HORIZON_UWP_TOUCH_MOD
        game_gyro_visible = gyro_visible;
#endif
        stage = "starting Horizon on the game worker";
        Windows::System::Threading::ThreadPool::RunAsync(
            ref new Windows::System::Threading::WorkItemHandler([resources, game, renderer](Windows::Foundation::IAsyncAction^) {
                try {
                    const int32_t result = horizon_uwp_run_prepared(
                        reinterpret_cast<const uint16_t*>(resources->Data()), resources->Length(),
                        reinterpret_cast<const uint16_t*>(game->Data()), game->Length(), renderer);
                      char finish_event[96];
                      sprintf_s(finish_event, "game bridge returned %d", result);
                      horizon_uwp_trace_shell_event(finish_event);
                    (void)result;
                    horizon_uwp_page_refresh_tile();
                } catch (Platform::Exception^ error) {
                    char message[320];
                    char detail[160] = {};
                    if (error->Message && error->Message->Length() > 0)
                        WideCharToMultiByte(CP_UTF8, 0, error->Message->Data(), -1, detail,
                            static_cast<int>(sizeof(detail)), nullptr, nullptr);
                    if (detail[0]) sprintf_s(message,
                        "Horizon encountered a Windows error on the game worker (0x%08X: %s). A diagnostic was saved in app storage.",
                        static_cast<unsigned>(error->HResult), detail);
                    else sprintf_s(message,
                        "Horizon encountered a Windows error on the game worker (0x%08X). A diagnostic was saved in app storage.",
                        static_cast<unsigned>(error->HResult));
                    report_native_failure(message);
                } catch (...) {
                    report_native_failure("Horizon encountered a native error on the game worker. A diagnostic was saved in app storage.");
                }
                if (primary_dispatcher) primary_dispatcher->RunAsync(
                    Windows::UI::Core::CoreDispatcherPriority::Normal,
                    ref new Windows::UI::Core::DispatchedHandler([]() { finish_game_in_primary_view(); }));
            }), Windows::System::Threading::WorkItemPriority::High,
            Windows::System::Threading::WorkItemOptions::TimeSliced);
        worker_started = true;
    } catch (Platform::Exception^ error) {
        char message[512];
        char exception_detail[160] = {};
        if (error->Message && error->Message->Length() > 0)
            WideCharToMultiByte(CP_UTF8, 0, error->Message->Data(), -1, exception_detail,
                                static_cast<int>(sizeof(exception_detail)), nullptr, nullptr);
        unsigned int page_detail_length = 0;
        char page_detail[192] = {};
        if (strcmp(stage, "showing the game surface over the launcher page") == 0)
            page_detail_length = static_cast<unsigned int>(horizon_uwp_page_switch_error(
                reinterpret_cast<uint8_t*>(page_detail), sizeof(page_detail)));
        const bool sending_initial_size = strcmp(stage, "sending the initial game size") == 0;
        const unsigned int size_stage = sending_initial_size ? horizon_uwp_window_size_stage() : 0;
        const char* size_operation = nullptr;
        char unknown_size_operation[64] = {};
        switch (size_stage) {
            case 1: size_operation = "reading DisplayInformation.LogicalDpi"; break;
            case 2: size_operation = "reading CoreWindow.Bounds"; break;
            case 3: size_operation = "sending the resize event"; break;
            case 4: size_operation = "finishing the initial size handoff"; break;
            default: break;
        }
        if (!size_operation && sending_initial_size) {
            sprintf_s(unknown_size_operation, "checking initial size (stage %u)", size_stage);
            size_operation = unknown_size_operation;
        }
        if (page_detail_length > 0)
            sprintf_s(message, "Horizon encountered a Windows error while %s (0x%08X: %s). A diagnostic was saved in app storage.",
                stage, static_cast<unsigned>(error->HResult), page_detail);
        else if (size_operation && exception_detail[0])
            sprintf_s(message, "Horizon encountered a Windows error while %s, %s (0x%08X: %s). A diagnostic was saved in app storage.",
                stage, size_operation, static_cast<unsigned>(error->HResult), exception_detail);
        else if (size_operation)
            sprintf_s(message, "Horizon encountered a Windows error while %s, %s (0x%08X). A diagnostic was saved in app storage.",
                stage, size_operation, static_cast<unsigned>(error->HResult));
        else if (exception_detail[0])
            sprintf_s(message, "Horizon encountered a Windows error while %s (0x%08X: %s). A diagnostic was saved in app storage.",
                stage, static_cast<unsigned>(error->HResult), exception_detail);
        else
            sprintf_s(message, "Horizon encountered a Windows error while %s (0x%08X). A diagnostic was saved in app storage.",
                stage, static_cast<unsigned>(error->HResult));
        report_native_failure(message);
    } catch (...) {
        char message[256];
        sprintf_s(message, "Horizon encountered a native error while %s. A diagnostic was saved in app storage.", stage);
        report_native_failure(message);
    }

    if (worker_started) return;

#ifdef HORIZON_UWP_TOUCH_MOD
    if (gyro_visible) horizon_uwp_gyro_set_visible(0);
    horizon_uwp_gyro_enable(0);
    game_gyro_visible = false;
#endif
    if (active_game_events) {
        delete active_game_events;
        active_game_events = nullptr;
    } else if (events) {
        delete events;
    }
    horizon_uwp_resume_pregame_input();
    if (phone && !was_full_screen && application_view) {
        try { application_view->ExitFullScreenMode(); }
        catch (Platform::Exception^) {}
    }
    if (page_hidden && !account_window_closed.load()) horizon_uwp_page_show_game(0);
    game_page_hidden = false;
    game_phone = false;
    game_was_full_screen = false;
    game_application_view = nullptr;
    game_running.store(false, std::memory_order_release);
}

extern "C" void __cdecl horizon_uwp_open_game_view() {
    horizon_uwp_trace_shell_event("open game view callback entered");
    try {
        run_game_in_primary_view();
    } catch (...) {
        report_native_failure("Horizon could not start Horizon in the app window.");
    }
}

extern "C" void __cdecl horizon_uwp_trace_shell_event(const char* event) {
    if (!event) return;
    try {
        auto local = Windows::Storage::ApplicationData::Current->LocalFolder->Path;
        auto path = Platform::String::Concat(local, L"\\horizon-uwp-lifecycle.log");
        HANDLE file = CreateFile2(path->Data(), FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, OPEN_ALWAYS, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        SYSTEMTIME now = {};
        GetSystemTime(&now);
        char line[512];
        const int length = sprintf_s(line, "%04u-%02u-%02uT%02u:%02u:%02u.%03uZ tid=%lu %s\r\n",
            now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond,
            now.wMilliseconds, GetCurrentThreadId(), event);
        if (length > 0) {
            DWORD written = 0;
            WriteFile(file, line, static_cast<DWORD>(length), &written, nullptr);
        }
        CloseHandle(file);
    } catch (...) {}
}
#endif

[Platform::MTAThread]
int main(Platform::Array<Platform::String^>^) {
#ifdef HORIZON_UWP_GAME_BRIDGE
    try {
#endif
#ifdef HORIZON_UWP_XAML_SHELL
    horizon_uwp_xaml_start();
#else
    CoreApplication::Run(ref new AppViewSource());
#endif
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

