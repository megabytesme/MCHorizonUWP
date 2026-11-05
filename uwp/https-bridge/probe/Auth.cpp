#include <ppltasks.h>
#include <string>
#include <windows.h>
#include <windows.ui.popups.h>
using namespace Concurrency;
extern "C" int __cdecl horizon_uwp_http_get(const unsigned char *, size_t,
                                            size_t, unsigned char **, size_t *);
extern "C" void __cdecl horizon_uwp_http_free(void *);
extern "C" int __cdecl horizon_uwp_http_request(const unsigned char *, size_t,
                                                int, const unsigned char *,
                                                size_t, const unsigned char *,
                                                size_t, const unsigned char *,
                                                size_t, size_t, unsigned int *,
                                                unsigned char **, size_t *);
using namespace Windows::ApplicationModel::Core;
using namespace Windows::ApplicationModel::Activation;
using namespace Windows::Foundation;
using namespace Windows::UI::Core;
using namespace Windows::Web::Http;
using namespace Windows::Storage;
ref class HttpsProbeView sealed : IFrameworkView {
  CoreWindow ^ window_;
  bool closed_ = false;
  bool started_ = false;
  HttpClient ^ client_;
  std::wstring result_;
  void save_and_display() {
    std::wstring path = ApplicationData::Current->LocalFolder->Path->Data();
    path += L"\\https-probe.txt";
    CREATEFILE2_EXTENDED_PARAMETERS args{};
    args.dwSize = sizeof(args);
    HANDLE h = CreateFile2(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ,
                           CREATE_ALWAYS, &args);
    if (h != INVALID_HANDLE_VALUE) {
      DWORD written;
      WriteFile(h, result_.data(),
                static_cast<DWORD>(result_.size() * sizeof(wchar_t)), &written,
                nullptr);
      CloseHandle(h);
    }
    (ref new Windows::UI::Popups::MessageDialog(
         ref new Platform::String(result_.c_str()), L"Horizon HTTPS probe"))
        ->ShowAsync();
  }
  void activated(CoreApplicationView ^, IActivatedEventArgs ^) {
    window_->Activate();
    if (started_)
      return;
    started_ = true;
    client_ = ref new HttpClient();
    auto context = task_continuation_context::use_current();
    create_task([]() -> std::wstring {
      auto request = [](const char *url, size_t limit, size_t *n) {
        unsigned char *body = nullptr;
        *n = 0;
        int status =
            horizon_uwp_http_get(reinterpret_cast<const unsigned char *>(url),
                                 strlen(url), limit, &body, n);
        if (body)
          horizon_uwp_http_free(body);
        return status;
      };
      const char *url = "https://httpbin.org/post";
      const char *body = "{\"probe\":true}";
      const char *auth = "Bearer fake-local-probe";
      const char *ctype = "application/json; charset=utf-8";
      unsigned int status = 0;
      unsigned char *out = nullptr;
      size_t n = 0;
      int result =
          horizon_uwp_http_request((const unsigned char *)url, strlen(url), 1,
                                   (const unsigned char *)auth, strlen(auth),
                                   (const unsigned char *)ctype, strlen(ctype),
                                   (const unsigned char *)body, strlen(body),
                                   1048576, &status, &out, &n);
      std::string reply;
      if (out) {
        reply.assign((char *)out, n);
        horizon_uwp_http_free(out);
      }
      bool echoed = reply.find("fake-local-probe") != std::string::npos &&
                    reply.find("probe") != std::string::npos;
      std::wstring text = L"POST result: " + std::to_wstring(result) +
                          L", status: " + std::to_wstring(status) +
                          L", fake headers/body echoed: " +
                          std::to_wstring(echoed) + L"\n";
      url = "https://httpbin.org/status/401";
      out = nullptr;
      n = 0;
      status = 0;
      result = horizon_uwp_http_request((const unsigned char *)url, strlen(url),
                                        0, nullptr, 0, nullptr, 0, nullptr, 0,
                                        1048576, &status, &out, &n);
      if (out)
        horizon_uwp_http_free(out);
      text += L"HTTP error request: " + std::to_wstring(result) +
              L", status: " + std::to_wstring(status) + L"\n";
      url = "https://expired.badssl.com/";
      out = nullptr;
      n = 0;
      status = 0;
      result = horizon_uwp_http_request((const unsigned char *)url, strlen(url),
                                        0, nullptr, 0, nullptr, 0, nullptr, 0,
                                        1048576, &status, &out, &n);
      if (out)
        horizon_uwp_http_free(out);
      text += L"Expired certificate result: " + std::to_wstring(result) + L"\n";
      url = "http://example.com/";
      out = nullptr;
      n = 0;
      status = 0;
      result = horizon_uwp_http_request((const unsigned char *)url, strlen(url),
                                        0, nullptr, 0, nullptr, 0, nullptr, 0,
                                        1048576, &status, &out, &n);
      if (out)
        horizon_uwp_http_free(out);
      text += L"Insecure scheme result: " + std::to_wstring(result);

      url = "https://httpbin.org/post";
      body = "test=sample&number=1";
      ctype = "application/x-www-form-urlencoded";
      out = nullptr;
      n = 0;
      status = 0;
      result =
          horizon_uwp_http_request((const unsigned char *)url, strlen(url), 1,
                                   nullptr, 0, (const unsigned char *)ctype,
                                   strlen(ctype), (const unsigned char *)body,
                                   strlen(body), 1048576, &status, &out, &n);
      reply.clear();
      if (out) {
        reply.assign((char *)out, n);
        horizon_uwp_http_free(out);
      }
      text += L"\nForm POST result: " + std::to_wstring(result) +
              L", status: " + std::to_wstring(status) + L", form parsed: " +
              std::to_wstring(reply.find("sample") != std::string::npos);
      url = "https://httpbin.org/redirect-to?url=https%3A%2F%2Fexample.com%2F";
      out = nullptr;
      n = 0;
      status = 0;
      result = horizon_uwp_http_request((const unsigned char *)url, strlen(url),
                                        0, (const unsigned char *)auth,
                                        strlen(auth), nullptr, 0, nullptr, 0,
                                        1048576, &status, &out, &n);
      if (out)
        horizon_uwp_http_free(out);
      text += L"\nAuthenticated redirect result: " + std::to_wstring(result) +
              L", status: " + std::to_wstring(status);
      url = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
      out = nullptr;
      n = 0;
      status = 0;
      result = horizon_uwp_http_request((const unsigned char *)url, strlen(url),
                                        0, nullptr, 0, nullptr, 0, nullptr, 0,
                                        10, &status, &out, &n);
      if (out)
        horizon_uwp_http_free(out);
      text += L"\nTen-byte response limit result: " + std::to_wstring(result);
      const char *injected = "Bearer fake\r\nX-Injected: bad";
      out = nullptr;
      n = 0;
      status = 0;
      result = horizon_uwp_http_request((const unsigned char *)url, strlen(url),
                                        0, (const unsigned char *)injected,
                                        strlen(injected), nullptr, 0, nullptr,
                                        0, 1048576, &status, &out, &n);
      if (out)
        horizon_uwp_http_free(out);
      text += L"\nInjected header result: " + std::to_wstring(result);
      url = "https://httpbin.org/status/204";
      out = nullptr;
      n = 0;
      status = 0;
      result = horizon_uwp_http_request((const unsigned char *)url, strlen(url),
                                        0, nullptr, 0, nullptr, 0, nullptr, 0,
                                        1048576, &status, &out, &n);
      if (out)
        horizon_uwp_http_free(out);
      text += L"\nEmpty 204 result: " + std::to_wstring(result) +
              L", status: " + std::to_wstring(status) + L", bytes: " +
              std::to_wstring(n);
      return text;
    })
        .then(
            [this](std::wstring result) {
              result_ = result;
              save_and_display();
            },
            context);
  }
  void closed(CoreWindow ^, CoreWindowEventArgs ^) { closed_ = true; }

public:
  virtual void Initialize(CoreApplicationView ^ view) {
    view->Activated +=
        ref new TypedEventHandler<CoreApplicationView ^, IActivatedEventArgs ^>(
            this, &HttpsProbeView::activated);
  }
  virtual void SetWindow(CoreWindow ^ window) {
    window_ = window;
    window->Closed +=
        ref new TypedEventHandler<CoreWindow ^, CoreWindowEventArgs ^>(
            this, &HttpsProbeView::closed);
  }
  virtual void Load(Platform::String ^) {}
  virtual void Run() {
    while (!closed_)
      window_->Dispatcher->ProcessEvents(
          CoreProcessEventsOption::ProcessOneAndAllPending);
  }
  virtual void Uninitialize() {}
};
ref class HttpsSource sealed : IFrameworkViewSource {
public:
  virtual IFrameworkView ^ CreateView() { return ref new HttpsProbeView(); }
};
[Platform::MTAThread] int main(Platform::Array<Platform::String ^> ^) {
  CoreApplication::Run(ref new HttpsSource());
  return 0;
}
