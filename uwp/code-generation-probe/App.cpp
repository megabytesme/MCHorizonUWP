#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <windows.h>

using namespace Windows::ApplicationModel::Activation;
using namespace Windows::ApplicationModel::Core;
using namespace Windows::Foundation;
using namespace Windows::Storage;
using namespace Windows::UI::Core;

namespace {
#if defined(_M_X64)
#ifndef HORIZON_UWP_WASMTIME_PROBE
constexpr uint8_t code[] = {0xb8, 42, 0, 0, 0, 0xc3}; // mov eax, 42; ret
#endif
constexpr char architecture[] = "x64";
#elif defined(_M_ARM64)
constexpr uint32_t code[] = {0x52800540, 0xd65f03c0}; // mov w0, #42; ret
constexpr char architecture[] = "arm64";
#elif defined(_M_ARM)
constexpr uint16_t code[] = {0x202a, 0x4770}; // movs r0, #42; bx lr
constexpr char architecture[] = "arm";
#else
#error Unsupported target architecture
#endif

#if defined(HORIZON_UWP_WASMTIME_PROBE)
extern "C" __declspec(dllimport) int wasmtime_uwp_probe();
#endif

void write_result(const char* stage, DWORD error, DWORD old_protection,
                  int value, DWORD release_error) {
    char result[256];
    const int size = sprintf_s(
        result, "{\"architecture\":\"%s\",\"stage\":\"%s\",\"win32_error\":%lu,"
                "\"old_protection\":%lu,\"value\":%d,\"release_error\":%lu}\n",
        architecture, stage, error, old_protection, value, release_error);
    if (size <= 0) return;

    auto path = ApplicationData::Current->LocalFolder->Path + L"\\result.json";
    auto file = CreateFile2(path->Data(), GENERIC_WRITE, FILE_SHARE_READ,
                            CREATE_ALWAYS, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    WriteFile(file, result, static_cast<DWORD>(size), &written, nullptr);
    CloseHandle(file);
}

void run_probe() {
#if defined(HORIZON_UWP_WASMTIME_PROBE)
    const int status = wasmtime_uwp_probe();
    write_result(status == 0 ? "pass" : "wasmtime_failure", 0, 0, status, 0);
#else
    void* region = VirtualAllocFromApp(nullptr, sizeof(code),
                                      MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (region == nullptr) {
        write_result("allocate_rw", GetLastError(), 0, 0, 0);
        return;
    }

    memcpy(region, code, sizeof(code));
    const char* stage = "pass";
    DWORD old_protection = 0;
    DWORD error = 0;
    int value = 0;
    if (!VirtualProtectFromApp(region, sizeof(code), PAGE_EXECUTE_READ,
                               &old_protection)) {
        stage = "protect_rx";
        error = GetLastError();
    } else if (!FlushInstructionCache(GetCurrentProcess(), region, sizeof(code))) {
        stage = "flush_instruction_cache";
        error = GetLastError();
    } else {
        auto address = reinterpret_cast<uintptr_t>(region);
#if defined(_M_ARM)
        address |= 1; // Call the generated code in Thumb state.
#endif
        value = reinterpret_cast<int(*)()>(address)();
        if (value != 42) stage = "unexpected_return";
    }
    const DWORD release_error = VirtualFree(region, 0, MEM_RELEASE) ? 0 : GetLastError();
    if (release_error != 0 && error == 0 && value == 42) stage = "release";
    write_result(stage, error, old_protection, value, release_error);
#endif
}
}

ref class ProbeView sealed : IFrameworkView {
    void activated(CoreApplicationView^, IActivatedEventArgs^) {
        CoreWindow::GetForCurrentThread()->Activate();
        run_probe();
        CoreApplication::Exit();
    }

public:
    virtual void Initialize(CoreApplicationView^ view) {
        view->Activated += ref new TypedEventHandler<CoreApplicationView^, IActivatedEventArgs^>(
            this, &ProbeView::activated);
    }
    virtual void SetWindow(CoreWindow^) {}
    virtual void Load(Platform::String^) {}
    virtual void Run() {
        CoreWindow::GetForCurrentThread()->Dispatcher->ProcessEvents(
            CoreProcessEventsOption::ProcessUntilQuit);
    }
    virtual void Uninitialize() {}
};

ref class ProbeSource sealed : IFrameworkViewSource {
public:
    virtual IFrameworkView^ CreateView() { return ref new ProbeView(); }
};

[Platform::MTAThread]
int main(Platform::Array<Platform::String^>^) {
    CoreApplication::Run(ref new ProbeSource());
    return 0;
}
