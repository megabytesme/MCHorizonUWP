#include <windows.h>
#include <appmodel.h>
#include <string>
#include <vector>

using namespace Windows::ApplicationModel;
using namespace Windows::ApplicationModel::Activation;
using namespace Windows::ApplicationModel::Core;
using namespace Windows::Foundation;
using namespace Windows::Storage;
using namespace Windows::UI::Core;

namespace {
std::wstring package_graph() {
    UINT32 size = 0;
    UINT32 count = 0;
    const UINT32 flags = PACKAGE_FILTER_HEAD | PACKAGE_FILTER_DIRECT | PACKAGE_FILTER_OPTIONAL;
    const LONG query = GetCurrentPackageInfo(flags, &size, nullptr, &count);
    if (query != ERROR_INSUFFICIENT_BUFFER)
        return L"package_graph_query_win32=" + std::to_wstring(query) + L"\r\n";
    std::vector<BYTE> buffer(size);
    const LONG read = GetCurrentPackageInfo(flags, &size, buffer.data(), &count);
    if (read != ERROR_SUCCESS)
        return L"package_graph_read_win32=" + std::to_wstring(read) + L"\r\n";
    std::wstring result;
    auto packages = reinterpret_cast<const PACKAGE_INFO*>(buffer.data());
    for (UINT32 i = 0; i < count; ++i)
        result += std::wstring(L"package=") + packages[i].packageFullName + L"\r\n";
    return result;
}

std::wstring load_probe() {
    HMODULE library = LoadPackagedLibrary(L"HorizonNativeModProbePayload.dll", 0);
    if (library == nullptr)
        return L"load_win32=" + std::to_wstring(GetLastError()) + L"\r\n";
    using Value = unsigned (*)();
    FARPROC symbol = GetProcAddress(library, "horizon_probe_value");
    const DWORD export_error = symbol ? ERROR_SUCCESS : GetLastError();
    const unsigned value = symbol ? reinterpret_cast<Value>(symbol)() : 0;
    const BOOL released = FreeLibrary(library);
    return L"load_win32=0\r\nexport_win32=" + std::to_wstring(export_error) +
           L"\r\nvalue=" + std::to_wstring(value) +
           L"\r\nreleased=" + std::to_wstring(released) + L"\r\n";
}

void save_result() {
    const auto version = Package::Current->Id->Version;
    const auto result = L"main_version=" + std::to_wstring(version.Major) + L"." +
        std::to_wstring(version.Minor) + L"." + std::to_wstring(version.Build) + L"." +
        std::to_wstring(version.Revision) + L"\r\n" + package_graph() + load_probe();
    const std::wstring path =
        std::wstring(ApplicationData::Current->LocalFolder->Path->Data()) + L"\\native-mod-probe.txt";
    HANDLE file = CreateFile2(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, CREATE_ALWAYS, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    const wchar_t bom = 0xFEFF;
    DWORD written = 0;
    WriteFile(file, &bom, sizeof(bom), &written, nullptr);
    WriteFile(file, result.data(), static_cast<DWORD>(result.size() * sizeof(wchar_t)), &written, nullptr);
    CloseHandle(file);
}
}

ref class ProbeView sealed : IFrameworkView {
    CoreWindow^ window_;

    void activated(CoreApplicationView^, IActivatedEventArgs^) {
        window_->Activate();
        save_result();
        CoreApplication::Exit();
    }

public:
    virtual void Initialize(CoreApplicationView^ view) {
        view->Activated += ref new TypedEventHandler<CoreApplicationView^, IActivatedEventArgs^>(
            this, &ProbeView::activated);
    }
    virtual void SetWindow(CoreWindow^ window) { window_ = window; }
    virtual void Load(Platform::String^) {}
    virtual void Run() {
        window_->Dispatcher->ProcessEvents(CoreProcessEventsOption::ProcessUntilQuit);
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
