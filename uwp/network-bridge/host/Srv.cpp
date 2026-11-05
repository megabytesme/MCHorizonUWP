// Resolve Minecraft's SRV service with the Windows network configuration.
// This function is called on Horizon's background networking threads.
#if defined(_MSC_VER) && _MSC_VER >= 1940 && defined(WINAPI_FAMILY)
extern "C" __declspec(dllimport) char* __cdecl getenv(const char*);
extern "C" __declspec(dllimport) int __cdecl system(const char*);
#endif
#include <windows.h>
#include <roapi.h>
#include <ppltasks.h>
#include <windows.networking.h>
#include <windows.networking.sockets.h>
#include <windows.system.threading.h>
#include <string>

using namespace Concurrency;
using namespace Windows::Networking;
using namespace Windows::Networking::Sockets;
using namespace Windows::System::Threading;

extern "C" int __cdecl horizon_uwp_resolve_srv(
    const unsigned char* host, size_t host_len, unsigned char* output,
    size_t capacity, size_t* output_len, unsigned short* port) {
    if (!host || !host_len || host_len > 253 || !output || !capacity || !output_len || !port)
        return -1;
    *output_len = 0;
    *port = 0;
    HRESULT init = RoInitialize(RO_INIT_MULTITHREADED);
    if (FAILED(init)) return -1;
    int result = -1;
    try {
        int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<const char*>(host), static_cast<int>(host_len), nullptr, 0);
        if (count <= 0 || count > 253) throw ref new Platform::InvalidArgumentException();
        std::wstring wide(count, L'\0');
        if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<const char*>(host), static_cast<int>(host_len), &wide[0], count))
            throw ref new Platform::InvalidArgumentException();
        auto name = ref new HostName(ref new Platform::String(wide.data(), count));
        auto operation = StreamSocket::GetEndpointPairsAsync(name, L"minecraft");
        Windows::Foundation::TimeSpan timeout;
        timeout.Duration = 5LL * 10000000;
        auto timer = ThreadPoolTimer::CreateTimer(
            ref new TimerElapsedHandler([operation](ThreadPoolTimer^) { operation->Cancel(); }), timeout);
        try {
            auto pairs = create_task(operation).get();
            timer->Cancel();
            if (pairs->Size) {
                auto pair = pairs->GetAt(0);
                auto target = pair->RemoteHostName->CanonicalName;
                auto service = pair->RemoteServiceName;
                unsigned long number = 0;
                for (unsigned int i = 0; i < service->Length(); ++i) {
                    wchar_t ch = service->Data()[i];
                    if (ch < L'0' || ch > L'9' || number > 65535 / 10) {
                        number = 0; break;
                    }
                    number = number * 10 + ch - L'0';
                    if (number > 65535) { number = 0; break; }
                }
                int bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS,
                    target->Data(), target->Length(), nullptr, 0, nullptr, nullptr);
                if (number && bytes > 0 && static_cast<size_t>(bytes) <= capacity &&
                    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, target->Data(),
                        target->Length(), reinterpret_cast<char*>(output), bytes, nullptr, nullptr) == bytes) {
                    *output_len = static_cast<size_t>(bytes);
                    *port = static_cast<unsigned short>(number);
                    result = 0;
                }
            }
        } catch (...) { timer->Cancel(); }
    } catch (...) {}
    RoUninitialize();
    return result;
}
